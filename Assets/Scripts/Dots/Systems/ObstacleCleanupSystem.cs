using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Dots.Systems
{
    public partial struct ObstacleCleanupSystem : ISystem
    {
        const byte OBSTACLE_COSTFIELD_VALUE = byte.MaxValue;
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BeginSimulationEntityCommandBufferSystem.Singleton>();
            state.RequireForUpdate<FlowFieldData>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.TryGetSingleton(out FlowFieldData flowFieldData))
            {
                Debug.LogError("No flow field data");
                return;
            }
            
            var costFieldBuffer = SystemAPI.GetSingletonBuffer<CostField>().Reinterpret<byte>().AsNativeArray();
            var obstacleCountBuffer = SystemAPI.GetSingletonBuffer<ObstacleCountMap>().Reinterpret<byte>().AsNativeArray();

            var ecbSingleton = SystemAPI.GetSingleton<BeginSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            
            foreach (var (obstacle, entity) in SystemAPI.Query<RefRO<ObstacleCleanup>>().WithNone<ObstacleTag>().WithEntityAccess())
            {
                
                int minX = math.clamp(obstacle.ValueRO.MinGridBound.x + (flowFieldData.GridSize.x / 2), 0, flowFieldData.GridSize.x - 1);
                int minY = math.clamp(obstacle.ValueRO.MinGridBound.y + (flowFieldData.GridSize.y / 2), 0 , flowFieldData.GridSize.y - 1);
                
                int maxX = math.clamp(obstacle.ValueRO.MaxGridBound.x + (flowFieldData.GridSize.x / 2), 0, flowFieldData.GridSize.x - 1);
                int maxY = math.clamp(obstacle.ValueRO.MaxGridBound.y + (flowFieldData.GridSize.y / 2), 0,  flowFieldData.GridSize.y - 1);
                
                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                    {
                        int costFieldIndex = y * flowFieldData.GridSize.x + x;
                        
                        if(obstacleCountBuffer[costFieldIndex] != 0) // This shouldn't occur but a check doesn't hurt
                            obstacleCountBuffer[costFieldIndex] -= 1;
                        
                        if (obstacleCountBuffer[costFieldIndex] == 0 && costFieldBuffer[costFieldIndex] == OBSTACLE_COSTFIELD_VALUE) // Safety check so we only adjust walls
                        {
                            costFieldBuffer[costFieldIndex] = 1; // Set the cell to walkable
                        }
                    }
                }

                ecb.RemoveComponent<ObstacleCleanup>(entity);
            }
        }
    }
}
