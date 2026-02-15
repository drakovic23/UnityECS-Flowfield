using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;

public partial struct CostFieldBakeSystem : ISystem
{
    const int LAYER_OBSTACLE = 7;
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<GridSettings>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<CostField>();
        state.RequireForUpdate<ObstacleCountMap>();
        state.RequireForUpdate<PerformBakeCostField>();
    }
    public void OnUpdate(ref SystemState state)
    {
        // Debug.Log("Running cost field bake");
        var gridConfig = SystemAPI.GetSingleton<GridSettings>();
        // int gridSize = gridConfig.GridSize.x *  gridConfig.GridSize.y;
        
        var performBake = SystemAPI.GetSingletonEntity<PerformBakeCostField>();
        var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        
        // Run the cost field job
        // Once complete, create a CostFieldReadyTag
        DynamicBuffer<byte> costFieldBuffer = SystemAPI.GetSingletonBuffer<CostField>().Reinterpret<byte>();
        DynamicBuffer<byte> obstacleCountBuffer = SystemAPI.GetSingletonBuffer<ObstacleCountMap>().Reinterpret<byte>();
        
        NativeArray<byte> costFieldArr = costFieldBuffer.AsNativeArray();
        NativeArray<byte> obstacleCountArr = obstacleCountBuffer.AsNativeArray();
        
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        CollisionWorld collisionWorld = physicsWorld.CollisionWorld;
        
        var costFieldHandle = new GenerateCostFieldJob{
            World = collisionWorld, CostField = costFieldArr, ObstacleCountMap = obstacleCountArr, Width = gridConfig.GridSize.x, Height = gridConfig.GridSize.y
        }.Schedule(gridConfig.TotalSize, 20, state.Dependency);

        costFieldHandle.Complete();
        
        // Let the other baker system know the cost field is ready
        ecb.RemoveComponent<PerformBakeCostField>(performBake);
        
        Entity readyEntity = ecb.CreateEntity();
        ecb.AddComponent(readyEntity, new CostFieldReadyTag());
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
}
