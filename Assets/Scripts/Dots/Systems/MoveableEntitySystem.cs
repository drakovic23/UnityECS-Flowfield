
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
public partial struct MoveableEntitySystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpatialMap>();
    }
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

        var spatialMap = SystemAPI.GetSingleton<SpatialMap>();
        foreach (var (moveableEntity, transform, velocity, entity) 
                 in SystemAPI.Query<RefRO<MoveableEntity>, RefRW<LocalTransform>, RefRW<PhysicsVelocity>>().WithEntityAccess())
        {
            float3 pos = transform.ValueRO.Position;
            // float3 playerPos = new float3()
            float distance = math.lengthsq(pos - playerTag.Position);
            
            
            
            // Movement logic
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

    public partial struct PopulateSpatialMapUpdateJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Entity>.ParallelWriter MapWriter;
        public int Width;
        public int Height;
        public int offsetX;
        public int offsetY;
        public void Execute(Entity entity, in LocalTransform transform)
        {
            int posX = (int)math.round(transform.Position.x) + offsetX;
            int posY = (int)math.round(transform.Position.z) + offsetY;
            if (posX < 0 || posX > Width - 1 || posY < 0 || posY > Height - 1)
                return;
            
            int cellindex = posY * Width + posX;
            
            MapWriter.Add(cellindex, entity);
        }
    }
    
    public partial struct ReadSpatialMapUpdateJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Entity> SpatialMap;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        public float SeparationRadius;
        public float SeparationWeight;
        public int Width;
        public int Height;
        public int OffsetX;
        public int OffsetY;
        public void Execute(Entity entity, ref PhysicsVelocity velocity, in LocalTransform transform)
        {
            int posX = (int)math.round(transform.Position.x) + OffsetX;
            int posY = (int)math.round(transform.Position.z) + OffsetY;
            if (posX < 0 || posX > Width - 1 || posY < 0 || posY > Height - 1)
                return;
            
            float3 separationForce = float3.zero;
            
            // Check 3 x 3 neighbors
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    int checkX = posX + x;
                    int checkY = posY + y;
                    
                    if (checkX < 0 || checkX >= Width || checkY < 0 || checkY >= Height)
                        continue;

                    int neighborIndex = checkY * Width + checkX;
                    
                    NativeParallelMultiHashMapIterator<int> iterator;
                    Entity neighbor;
                    
                    if (SpatialMap.TryGetFirstValue(neighborIndex, out neighbor, out iterator))
                    {
                        do
                        {
                            // Skip ourselves
                            if (neighbor == entity)
                                continue;
                            
                            float3 neighborPos = TransformLookup[neighbor].Position;
                            float distance = math.lengthsq(transform.Position - neighborPos);
                            
                            if (distance < math.square(SeparationRadius)) // Square the radius so we have equal comparison
                            {
                                // flatten our current and neighbor positions
                                float3 horizontalPosCurrent = new float3(transform.Position.x, 0, transform.Position.z);
                                float3 horizontalPosNeighbor = new float3(neighborPos.x, 0, neighborPos.z);
                                separationForce += math.normalizesafe(horizontalPosCurrent - horizontalPosNeighbor);
                            }
                        } while (SpatialMap.TryGetNextValue(out neighbor, ref iterator));
                    }
                }
            }
            
            // Apply the force
            float MaxSpeed = 5f;
            if (!math.all(separationForce == 0))
            {
                velocity.Linear += separationForce * SeparationWeight;

                float2 horizontalVelocity = new float2(velocity.Linear.x, velocity.Linear.z);
                if(math.lengthsq(horizontalVelocity) > math.square(MaxSpeed))
                {
                    float3 horizontalVelocityScaled = math.normalizesafe(new float3(horizontalVelocity.x, 0, horizontalVelocity.y)) * MaxSpeed;
                    float3 newVelocity = new float3(horizontalVelocityScaled.x, velocity.Linear.y, horizontalVelocityScaled.z);
                    
                    velocity.Linear = newVelocity;
                }
            }
        }
    }
}

/*  --------------------Notes---------------------
    Currently, the entity just moves towards the target
    When using hordes there is typically 3 parts to accomodate large hordes
    (1) Separation - If a neighbor (ex: left neighbor) is too close to the entity, we need to apply a vector in the opposite direction (right)
    (2) Alignment - Since the entity is assumed to move in "crowds", it would be optimal to steer towards an average vector of the entire crowd
    (3) Cohesion - If the entity drifts away from the crowd, we need to move towards the average position of the crowd (center of mass)
    The flow field acts as alignment (entities are pulled together toward the target) and as alignment (the entities are forced to face the same direction)
    As a result we can use flow field + separation to achieve similar results since the separation and alignment are redundant math
    ----------------------------------------------
*/