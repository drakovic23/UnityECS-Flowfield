using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

public partial struct BakeGridSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<SimulationSingleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        // Buffers
        state.RequireForUpdate<CostField>();
        state.RequireForUpdate<ObstacleCountMap>();
        state.RequireForUpdate<IntegrationField>();
        state.RequireForUpdate<FlowFieldDirection>();
        state.RequireForUpdate<GridSettings>();

        state.RequireForUpdate<CostFieldReadyTag>();
    }
    
    public void OnUpdate(ref SystemState state)
    {
        // Get our grid size and width
        var gridConfig = SystemAPI.GetSingleton<GridSettings>();
        int gridSize = gridConfig.TotalSize;
        int width = gridConfig.GridSize.x;
        int height = gridConfig.GridSize.y;

        if (gridSize == 0) // error
            return;
        
        foreach (var (tag, entity) in SystemAPI.Query<RefRO<PerformBakeTag>>().WithEntityAccess())
        {
            DynamicBuffer<byte> costFieldBuffer = SystemAPI.GetSingletonBuffer<CostField>().Reinterpret<byte>();
            // DynamicBuffer<byte> obstacleCountBuffer = SystemAPI.GetSingletonBuffer<ObstacleCountMap>().Reinterpret<byte>();
            DynamicBuffer<ushort> integrationFieldBuffer = SystemAPI.GetSingletonBuffer<IntegrationField>().Reinterpret<ushort>();
            DynamicBuffer<float2> flowFieldBuffer = SystemAPI.GetSingletonBuffer<FlowFieldDirection>().Reinterpret<float2>();
            
            integrationFieldBuffer.ResizeUninitialized(gridSize);
            flowFieldBuffer.ResizeUninitialized(gridSize);
            
            // Cache the pointers
            NativeArray<byte> costFieldArr = costFieldBuffer.AsNativeArray();
            NativeArray<ushort> integrationFieldArr = integrationFieldBuffer.AsNativeArray();
            NativeArray<float2> flowFieldArr = flowFieldBuffer.AsNativeArray();
            
            
            NativeArray<int2> neighborOffsets = new NativeArray<int2>(4, Allocator.TempJob);
            neighborOffsets[0] = new int2(0, 1);
            neighborOffsets[1] = new int2(0, -1);
            neighborOffsets[2] = new int2(-1, 0);
            neighborOffsets[3] = new int2(1, 0);
            
            int2 targetCell = new int2(width / 2, height / 2); // Default to 0, 0
            if (SystemAPI.TryGetSingleton(out PlayerTag playerTag))
            {
                targetCell.x = (int)math.round(playerTag.Position.x);
                targetCell.y = (int)math.round(playerTag.Position.z);
                targetCell.x += (width / 2);
                targetCell.y += (height / 2);
            }
            else
            {
                Debug.LogError("No player tag found");
            }
            
            // Schedule jobs
            var integrationFieldHandle = new GenerateIntegrationField{
                CostField = costFieldArr,
                Height = height,
                Width = width,
                IntegrationField = integrationFieldArr,
                TargetCell = targetCell
            }.Schedule(state.Dependency);
            var flowFieldHandle = new GenerateFlowFieldJob{
                FlowField = flowFieldArr,
                Height = height,
                Width = width,
                IntegrationField = integrationFieldArr,
                NeighborOffsets = neighborOffsets
            }.Schedule(gridSize, 20, integrationFieldHandle);
            flowFieldHandle.Complete();
            
            neighborOffsets.Dispose();
            
            // Remove the tag
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            ecb.RemoveComponent<PerformBakeTag>(entity);
            
            // Debugging the flow field
            for (int i = 0; i < flowFieldArr.Length; i++)
            {
                if (math.lengthsq(flowFieldArr[i]) < 0.0001f)
                    continue;

                int gridX = i % width;
                int gridY = i / height;
                
                float3 cellCenter = new float3(
                gridX - gridConfig.OffsetX, 
                0.2f,
                gridY - gridConfig.OffsetY
                );
                
                float3 direction = new float3(flowFieldArr[i].x, 0, flowFieldArr[i].y);
                float3 start = cellCenter - (direction * 0.5f);

                if (integrationFieldArr[i] == byte.MaxValue)
                {
                    Debug.DrawRay(start, direction, Color.purple, 3f);
                    continue;
                }
                Debug.DrawRay(start, direction, Color.aquamarine, 3f);
            }
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
            // UnsafeUtility.MemSet(IntegrationField.GetUnsafePtr(), 0xFF, IntegrationField.Length * sizeof(ushort));
            
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
                    // Check if we are checking neighbors at an edge
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
            
            ushort currentCost = IntegrationField[index];

            if (currentCost == ushort.MaxValue)
            {
                FlowField[index] = float2.zero;
                return;
            }
            // We have to check if we are at the edges of our grid.
            // If we are given coord we have to flatten it so we check currentX against index - 1 (for left) and index + 1 (for right) neighbors.
            ushort leftCost = (currentX > 0) ? IntegrationField[index - 1] : ushort.MaxValue;
            if (leftCost == ushort.MaxValue) leftCost = currentCost;
            ushort rightCost = (currentX < Width - 1) ? IntegrationField[index + 1] : ushort.MaxValue;
            if (rightCost == ushort.MaxValue) rightCost = currentCost;
            ushort downCost = (currentY > 0) ? IntegrationField[index - Width] : ushort.MaxValue;
            if (downCost == ushort.MaxValue) downCost = currentCost;
            ushort upCost = (currentY < Height - 1) ? IntegrationField[index + Width] : ushort.MaxValue;
            if (upCost == ushort.MaxValue) upCost = currentCost;

            // Calculate the slope/gradient so we get diagonals
            float xDir = leftCost - rightCost;
            float yDir = downCost - upCost;
            
            float2 direction = new float2(xDir, yDir);

            if (math.lengthsq(direction) < 0.001f)
            {
                FlowField[index] = float2.zero;
            }
            else
            {
                FlowField[index] = math.normalizesafe(direction);
            }
        }
    }
}
