using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

public partial struct BakeGridSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }
    const int LAYER_OBSTACLE = 7;

    public void OnUpdate(ref SystemState state)
    {
        // Get our grid size and width
        int gridSize = 0;
        int width = 0;
        int height = 0;
        foreach (var gridConfig in SystemAPI.Query<RefRO<FlowFieldData>>())
        {
            gridSize = gridConfig.ValueRO.GridSize.x *  gridConfig.ValueRO.GridSize.y;
            width = gridConfig.ValueRO.GridSize.x;
            height = gridConfig.ValueRO.GridSize.y;
        }

        if (gridSize == 0) // error
            return;
        
        // Let's assume we only bake when a bake tag exists
        foreach (var (tag, entity) in SystemAPI.Query<RefRO<PerformBakeTag>>().WithEntityAccess())
        {
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            CollisionWorld collisionWorld = physicsWorld.CollisionWorld;
            
            // Allocate a temporary buffer for the cost field and integration field
            NativeArray<byte> tempCostFieldBuffer = new NativeArray<byte>(gridSize, Allocator.TempJob);
            NativeArray<byte> tempObstacleBuffer = new NativeArray<byte>(gridSize, Allocator.TempJob);
            NativeArray<ushort> tempIntegrationBuffer = new NativeArray<ushort>(gridSize, Allocator.TempJob);
            NativeArray<float2> tempFlowFieldBuffer = new NativeArray<float2>(gridSize, Allocator.TempJob);
            
            NativeArray<int2> neighborOffsets = new NativeArray<int2>(4, Allocator.TempJob);
            neighborOffsets[0] = new int2(0, 1);
            neighborOffsets[1] = new int2(0, -1);
            neighborOffsets[2] = new int2(-1, 0);
            neighborOffsets[3] = new int2(1, 0);
            
            int2 targetCell = new int2(width / 2, height / 2); // Default to 0, 0
            if (SystemAPI.TryGetSingleton(out PlayerTag playerTag))
            {
                targetCell = new int2(Mathf.RoundToInt(playerTag.Position.x), Mathf.RoundToInt(playerTag.Position.z));
                targetCell.x += (width / 2);
                targetCell.y += (height / 2);
            }
            else
            {
                Debug.LogError("No player tag found");
            }
            // Schedule jobs
            var costFieldHandle = new GenerateCostFieldJob{
                World = collisionWorld, CostField = tempCostFieldBuffer, ObstacleCountMap = tempObstacleBuffer, Width = width, Height = height
            }.Schedule(gridSize, 20, state.Dependency);
            var integrationFieldHandle = new GenerateIntegrationField{
                CostField = tempCostFieldBuffer, // Using the temp buffer here is faster since .AsNativeArray() returns a pointer
                Height = height,
                Width = width,
                IntegrationField = tempIntegrationBuffer,
                TargetCell = targetCell
            }.Schedule(costFieldHandle);
            var flowFieldHandle = new GenerateFlowFieldJob{
                FlowField = tempFlowFieldBuffer,
                Height = height,
                Width = width,
                IntegrationField = tempIntegrationBuffer,
                NeighborOffsets = neighborOffsets
            }.Schedule(gridSize, 20, integrationFieldHandle);
            flowFieldHandle.Complete();
            
            // Copy our temp buffer into their respective fields
            DynamicBuffer<byte> costFieldBuffer = SystemAPI.GetSingletonBuffer<CostField>().Reinterpret<byte>();
            DynamicBuffer<byte> obstacleMapBuffer = SystemAPI.GetSingletonBuffer<ObstacleCountMap>().Reinterpret<byte>();
            DynamicBuffer<ushort> integrationFieldBuffer = SystemAPI.GetSingletonBuffer<IntegrationField>().Reinterpret<ushort>();
            DynamicBuffer<FlowFieldDirection> flowField = SystemAPI.GetSingletonBuffer<FlowFieldDirection>(); // remove this later
            DynamicBuffer<float2> flowFieldBuffer = SystemAPI.GetSingletonBuffer<FlowFieldDirection>().Reinterpret<float2>();
            
            // Copy the data
            costFieldBuffer.CopyFrom(tempCostFieldBuffer);
            integrationFieldBuffer.CopyFrom(tempIntegrationBuffer);
            flowFieldBuffer.CopyFrom(tempFlowFieldBuffer);
            obstacleMapBuffer.CopyFrom(tempObstacleBuffer);
            
            // Cleanup
            tempIntegrationBuffer.Dispose();
            tempCostFieldBuffer.Dispose();
            tempFlowFieldBuffer.Dispose();
            neighborOffsets.Dispose();
            tempObstacleBuffer.Dispose();
            
            // Remove the tag so we bake only once for now
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            ecb.RemoveComponent<PerformBakeTag>(entity);
            
            // Just for debugging the cost field
            // Debug.Log("Performed cost bake");
            
            for (int i = 0; i < costFieldBuffer.Length; i++)
            {
                if (costFieldBuffer[i] == byte.MaxValue)
                {
                    int x = i % width;
                    int y = i / width;
                    int offsetX = width / 2;
                    int offsetY = width / 2;
                    
                    Debug.DrawLine(new Vector3(x - offsetX, 10, y - offsetY), new Vector3(x - offsetX, 0, y - offsetY), Color.red, 30f);
                }
            }
            
            // Debugging the flow field
            for (int i = 0; i < flowField.Length; i++)
            {
                if (math.lengthsq(flowField[i].Direction) < 0.001f)
                    continue;

                int gridX = i % width;
                int gridY = i / height;
                
                float3 start = new float3(gridX - (width / 2), 1.0f, (gridY - (height / 2)));
                
                Debug.DrawRay(start, new float3(flowField[i].Direction.x, 0,  flowField[i].Direction.y), Color.aquamarine, 3f);
            }
        }
    }
    public struct GenerateCostFieldJob : IJobParallelFor
    {
        [ReadOnly] public CollisionWorld World;
        [WriteOnly] public NativeArray<byte> CostField;
        [WriteOnly] public NativeArray<byte> ObstacleCountMap;
        public int Width;
        public int Height;
        public void Execute(int index)
        {
            int x = index % Width;
            int y = index / Height;
            uint obstacleMask = (1u << LAYER_OBSTACLE);

            var collisionFilter = new CollisionFilter{
                BelongsTo = ~0u, CollidesWith = obstacleMask, GroupIndex = 0
            };
            
            // Get the center position of the cell
            // float3 center = new float3(x + 0.5f, 0, y + 0.5f);
            int offsetX = Width / 2;
            int offsetY = Height / 2;
            float3 center = new float3(x - offsetX, 0.1f, y - offsetY);

            NativeList<DistanceHit> hits = new NativeList<DistanceHit>(Allocator.Temp);
            hits.Resize(5, NativeArrayOptions.ClearMemory);
            
            // To see where the sphere is hitting at
            // Debug.DrawLine(new float3(center.x, 10f, center.y), center, Color.blue, 25f);
            
            if(World.OverlapSphere(center, 0.45f, ref hits, collisionFilter, QueryInteraction.Default))
            {
                // if (x - offsetX == -2 && y - offsetY == 0) 
                // {
                //     float3 hitPoint = hits[0].Position;
                //     // Debug.Log($"Query At: {center} | Hit Surface At: {hitPoint} | Distance: {math.distance(center, hitPoint)}");
                // }
                CostField[index] = byte.MaxValue;
                ObstacleCountMap[index] = (byte) hits.Length;
            }
            else
            {
                CostField[index] = 1;
            }

            hits.Dispose();
        }
    }
    
    public struct GenerateIntegrationField : IJob
    {
        [ReadOnly] public NativeArray<byte> CostField;
        
        public NativeArray<ushort> IntegrationField;
        public int2 TargetCell;
        public int Width;
        public int Height;
        public void Execute()
        {
            // Reset our IntegrationField
            for (int i = 0; i < IntegrationField.Length; i++)
            {
                IntegrationField[i] = ushort.MaxValue;
            }
            
            // Set our target cell to value 0
            int targetIndex = TargetCell.y * Width + TargetCell.x;
            IntegrationField[targetIndex] = 0;
            // Add our target cell to the queue
            NativeQueue<int> visitedCells = new NativeQueue<int>(Allocator.Temp);
            visitedCells.Enqueue(targetIndex);

            NativeArray<int2> neighborOffsets = new NativeArray<int2>(4, Allocator.Temp);
            neighborOffsets[0] = new int2(0, 1);
            neighborOffsets[1] = new int2(0, -1);
            neighborOffsets[2] = new int2(-1, 0);
            neighborOffsets[3] = new int2(1, 0);
            
            // Path through the other cells
            while (visitedCells.Count > 0)
            {
                int currentCell = visitedCells.Dequeue();
                
                int currentX = currentCell % Width;
                int currentY = currentCell / Width;

                for (int i = 0; i < 4; i++)
                {
                    int2 neighbor = new int2(currentX, currentY) + neighborOffsets[i];
                    // Since our grid is centered around 0,0 we need to check if we are checking neighbors at an edge
                    if (neighbor.x < 0 || neighbor.x > Width - 1 || neighbor.y < 0 || neighbor.y > Height - 1)
                        continue;
                    
                    int neighborCell = neighbor.y * Width + neighbor.x;
                    // Check bounds
                    if (neighborCell < IntegrationField.Length)
                    {
                        // Is the neighbor walkable
                        if (CostField[neighborCell] < byte.MaxValue)
                        {
                            // Is the neighbor cheaper
                            int newCost = IntegrationField[currentCell] + CostField[neighborCell];
                            
                            if (newCost < IntegrationField[neighborCell])
                            {
                                IntegrationField[neighborCell] = (ushort) newCost;
                                visitedCells.Enqueue(neighborCell);
                            }
                        }
                    }
                }
            }
            visitedCells.Dispose();
            neighborOffsets.Dispose();
        }
    }
    
    public struct GenerateFlowFieldJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<ushort> IntegrationField;
        [WriteOnly] public NativeArray<float2> FlowField;
        [ReadOnly] public NativeArray<int2> NeighborOffsets;
        public int Width;
        public int Height;
        
        public void Execute(int index)
        {
            
            int currentX = index % Width, currentY = index / Width;
            
            // We have to check if we are at the edges of our grid.
            // If we are given coord we have to flatten it so we check currentX against index - 1 (for left) and index + 1 (for right) neighbors.
            ushort leftCost = (currentX > 0) ? IntegrationField[index - 1] : ushort.MaxValue;
            ushort rightCost = (currentX < Width - 1) ? IntegrationField[index + 1] : ushort.MaxValue;
            ushort downCost = (currentY > 0) ? IntegrationField[index - Width] : ushort.MaxValue;
            ushort upCost = (currentY < Height - 1) ? IntegrationField[index + Width] : ushort.MaxValue;

            // Calculate the slope/gradient
            float xDir = leftCost - rightCost;
            float yDir = downCost - upCost;
            
            float2 direction = new float2(xDir, yDir);

            // Since we can get a large vector here, normalize
            FlowField[index] = math.normalizesafe(direction);

            // This method will always choose the Z axis over the X axis due to the way we check neighbors
            // Thus we have no gradients, unless an obstacle is in the way
            // int currentCost = IntegrationField[index];
            // ushort bestCost = (ushort)currentCost;
            // float2 bestDirection = float2.zero;
            // for (int i = 0; i < 4; i++)
            // {
            //     int2 currentNeighbor = new int2(currentX, currentY) + NeighborOffsets[i];
            //     if (currentNeighbor.x < 0 || currentNeighbor.x > Width - 1 || currentNeighbor.y < 0 || currentNeighbor.y > Height - 1)
            //         continue;
            //
            //     int neighborCell = currentNeighbor.y * Width + currentNeighbor.x;
            //     if (IntegrationField[neighborCell] < bestCost)
            //     {
            //         bestCost = IntegrationField[neighborCell];
            //         bestDirection = NeighborOffsets[i];
            //     }
            // }

            // FlowField[index] = bestDirection;
        }
    }
}
