
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
public partial struct MoveableEntitySystem : ISystem
{
    NativeParallelMultiHashMap<int, Entity> _spatialMap;
    EntityQuery _moveablesQuery;
    FlowFieldData _flowFieldData;
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FlowFieldData>();
        _spatialMap = new NativeParallelMultiHashMap<int, Entity>(1000,  Allocator.Persistent);
        _moveablesQuery = SystemAPI.QueryBuilder().WithAll<MoveableEntity, PhysicsVelocity, LocalTransform>().Build();
    }
    public void OnDestroy(ref SystemState state)
    {
        _spatialMap.Dispose();
    }
    public void OnUpdate(ref SystemState state)
    {
        int width = 0;
        int height = 0;
        int offsetX = 0;
        int offsetY = 0;
        
        // The player tag is used to stop the entity around the target
        if (!SystemAPI.TryGetSingleton(out PlayerTag playerTag))
        {
            Debug.LogError("No player tag");
            return;
        }
        
        if (!SystemAPI.TryGetSingleton(out FlowFieldData flowFieldData))
        {
            Debug.LogError("No flow field data");
            return;
        }

        ComponentLookup<LocalTransform> localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        var flowFieldDirection = SystemAPI.GetSingletonBuffer<FlowFieldDirection>();
        width = flowFieldData.GridSize.x;
        height = flowFieldData.GridSize.y;
        offsetX = width / 2;
        offsetY = height / 2;
        
        // Clear the map and schedule jobs
        _spatialMap.Clear();

        var populateSpatialHandle = new PopulateSpatialMapUpdateJob{
            Height = height,
            Width = width,
            SpatialMap = _spatialMap.AsParallelWriter(),
            offsetX = offsetX,
            offsetY = offsetY,
        }.ScheduleParallel(_moveablesQuery, state.Dependency);
        
        populateSpatialHandle.Complete();
        
        Debug.Log("Populate spatial completed");
        // This will move the entities
        var readSpatialHandle = new ReadSpatialMapUpdateJob{
            Height = height,
            Width = width,
            SpatialMap = _spatialMap,
            TransformLookup = localTransformLookup,
            SeparationRadius = 0.8f,
            SeparationWeight = 0.8f,
            OffsetX = offsetX,
            OffsetY = offsetY,
        }.ScheduleParallel(_moveablesQuery, populateSpatialHandle);
        state.Dependency = readSpatialHandle;
        readSpatialHandle.Complete();
        
        int count = 0;
        // Handle the actual movement of our entities
        foreach (var (moveableEntity, transform, velocity, entity)
                 in SystemAPI.Query<RefRO<MoveableEntity>, RefRW<LocalTransform>, RefRW<PhysicsVelocity>>().WithEntityAccess())
        {
            // Get the current position of our entity
            // Read from the spatial map
            // Move the entity
        
            float3 position = transform.ValueRO.Position;
            int posX = (int)math.round(position.x) + offsetX;
            int posY = (int)math.round(position.z) + offsetY;
        
            if (posX < 0 || posX > width - 1 || posY < 0 || posY > height - 1)
                continue;
            
            int cellIndex = posY * width + posX;
            float2 direction = flowFieldDirection[cellIndex].Direction;

            velocity.ValueRW.Linear = new float3(direction.x, velocity.ValueRO.Linear.y, direction.y);
            count += 1;
        }

        Debug.Log(count);
    }
    
    public partial struct PopulateSpatialMapUpdateJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Entity>.ParallelWriter SpatialMap;
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
            
            SpatialMap.Add(cellindex, entity);
        }
    }
    
    public partial struct ReadSpatialMapUpdateJob : IJobEntity
    {
        [ReadOnly] public NativeParallelMultiHashMap<int, Entity> SpatialMap;
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
                            float distanceSq = math.lengthsq(transform.Position - neighborPos);
                            
                            if (distanceSq < math.square(SeparationRadius)) // Square the radius so we have equal comparison
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
                // Are we moving faster than we should?
                if(math.lengthsq(horizontalVelocity) > math.square(MaxSpeed))
                {
                    // Flatten the vectors to keep our vertical gravity
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