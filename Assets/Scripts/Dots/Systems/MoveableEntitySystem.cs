
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
public partial struct MoveableEntitySystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        // int gridSize = 0;
        int width = 0;
        int height = 0;
        float deltaTime = SystemAPI.Time.DeltaTime;
        foreach (var gridConfig in SystemAPI.Query<RefRO<FlowFieldData>>())
        {
            // gridSize = gridConfig.ValueRO.GridSize.x *  gridConfig.ValueRO.GridSize.y;
            width = gridConfig.ValueRO.GridSize.x;
            height = gridConfig.ValueRO.GridSize.y;
        }

        int offsetX = width / 2;
        int offsetY = height / 2;
        
        foreach (var (moveableEntity, transform) 
                 in SystemAPI.Query<RefRO<MoveableEntity>, RefRW<LocalTransform>>())
        {
            // Get our position and round
            float3 pos = transform.ValueRO.Position;
            int posX = Mathf.RoundToInt(pos.x) + offsetX;
            int posY = Mathf.RoundToInt(pos.y) + offsetY;
            
            DynamicBuffer<FlowFieldDirection> flowBuffer = SystemAPI.GetSingletonBuffer<FlowFieldDirection>();

            if (posX < 0 || posX > width - 1 || posY < 0 || posY > height - 1)
                continue;
            
            // Convert to world space to array space
            int currentCell = posY * width + posX;
            
            float3 direction = new float3(flowBuffer[currentCell].Direction.x, 0,  flowBuffer[currentCell].Direction.y);
            transform.ValueRW.Position += direction * deltaTime;
        }
    }
}
