
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
public partial struct MoveableEntitySystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        int width = 0;
        int height = 0;
        foreach (var gridConfig in SystemAPI.Query<RefRO<FlowFieldData>>())
        {
            // gridSize = gridConfig.ValueRO.GridSize.x *  gridConfig.ValueRO.GridSize.y;
            width = gridConfig.ValueRO.GridSize.x;
            height = gridConfig.ValueRO.GridSize.y;
        }

        int offsetX = width / 2;
        int offsetY = height / 2;
        if (!SystemAPI.TryGetSingleton(out PlayerTag playerTag))
        {
            Debug.LogError("No player tag");
            return;
        }

        foreach (var (moveableEntity, transform, velocity) 
                 in SystemAPI.Query<RefRO<MoveableEntity>, RefRW<LocalTransform>, RefRW<PhysicsVelocity>>())
        {
            float3 pos = transform.ValueRO.Position;
            // float3 playerPos = new float3()
            float distance = math.lengthsq(pos - playerTag.Position);

            if (distance < 1f)
            {
                Debug.LogWarning("Entity reached target");
                return;
            }
            // Get our position and round
            int posX = Mathf.RoundToInt(pos.x) + offsetX;
            int posY = Mathf.RoundToInt(pos.z) + offsetY;
            
            DynamicBuffer<FlowFieldDirection> flowBuffer = SystemAPI.GetSingletonBuffer<FlowFieldDirection>();

            if (posX < 0 || posX > width - 1 || posY < 0 || posY > height - 1)
                continue;
            
            // Convert to world space to array space
            int currentCell = posY * width + posX;
            
            // Apply our direction
            float3 direction = new float3(flowBuffer[currentCell].Direction.x, velocity.ValueRW.Linear.y, flowBuffer[currentCell].Direction.y);
            velocity.ValueRW.Linear = new float3(direction.x, velocity.ValueRW.Linear.y, direction.z);
            // float3 direction = new float3(flowBuffer[currentCell].Direction.x, 0,  flowBuffer[currentCell].Direction.y);
            // transform.ValueRW.Position += direction * deltaTime;
            
            Debug.Log($"Move direction: {direction} at {pos}");
            
        }
    }
}

// --------- Notes ---------
// Currently, the entity just moves towards the target
// When using hordes there is typically 3 parts to accomodate large hordes
// (1) Separation - If a neighbor (ex: left neighbor) is too close to the entity, we need to apply a vector in the opposite direction (right)
// (2) Alignment - Since the entity is assumed to move in "crowds", it would be optimal to steer towards an average vector of the entire crowd
// (3) Cohesion - If the entity drifts away from the crowd, we need to move towards the average position of the crowd (center of mass)