
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;
public partial struct MoveableEntitySystem : ISystem
{
    NativeParallelMultiHashMap<int, Entity> _spatialMap;
    EntityQuery _moveablesQuery;
    EntityQuery _moveablesQueryNoVelocity;
    FlowFieldData _flowFieldData;
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<FlowFieldData>();
        _spatialMap = new NativeParallelMultiHashMap<int, Entity>(1000,  Allocator.Persistent);
        _moveablesQuery = SystemAPI.QueryBuilder().WithAll<MoveableEntity, PhysicsVelocity, LocalTransform>().Build();
        _moveablesQueryNoVelocity = SystemAPI.QueryBuilder().WithAll<MoveableEntity, LocalTransform>().Build();
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
        
        // Used to stop the entity around the target
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
        
        var flowFieldDirection = SystemAPI.GetSingletonBuffer<FlowFieldDirection>();
        var costField = SystemAPI.GetSingletonBuffer<CostField>();
        width = flowFieldData.GridSize.x;
        height = flowFieldData.GridSize.y;
        offsetX = width / 2;
        offsetY = height / 2;
        
        // Clear the map and schedule jobs
        // It's possible that the other threads haven't yet completed
        // So we wait for any dependencies
        var clearSpatialHandle = new ClearSpatialMap{
            SpatialMap =  _spatialMap,
        }.Schedule(state.Dependency);

        var populateSpatialHandle = new PopulateSpatialMapUpdateJob{
            Height = height,
            Width = width,
            SpatialMap = _spatialMap.AsParallelWriter(),
            OffsetX = offsetX,
            OffsetY = offsetY,
        }.ScheduleParallel(_moveablesQueryNoVelocity, clearSpatialHandle);
        populateSpatialHandle.Complete();
        
        // This will move the entities
        ComponentLookup<LocalTransform> localTransformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        ComponentLookup<PhysicsVelocity> velocityLookup = SystemAPI.GetComponentLookup<PhysicsVelocity>(true);
        var readSpatialHandle = new ReadSpatialMapUpdateJob{
            Height = height,
            Width = width,
            SpatialMap = _spatialMap,
            FlowFieldDirection = flowFieldDirection.Reinterpret<float2>().AsNativeArray(),
            CostField = costField.Reinterpret<byte>().AsNativeArray(),
            TransformLookup = localTransformLookup,
            VelocityLookup = velocityLookup,
            SeparationRadius = 0.8f,
            SeparationWeight = 0.8f,
            AlignmentWeight = 0.1f,
            CohesionWeight = 1.5f,
            LookAheadTime = 0.5f,
            AvoidanceWeight = 0.5f,
            OffsetX = offsetX,
            OffsetY = offsetY,
            DeltaTime = SystemAPI.Time.DeltaTime,
        }.ScheduleParallel(_moveablesQuery, populateSpatialHandle);
        state.Dependency = readSpatialHandle;
    }
    
    public partial struct PopulateSpatialMapUpdateJob : IJobEntity
    {
        public NativeParallelMultiHashMap<int, Entity>.ParallelWriter SpatialMap;
        public int Width;
        public int Height;
        public int OffsetX;
        public int OffsetY;
        public void Execute(Entity entity, in LocalTransform transform)
        {
            int posX = (int)math.round(transform.Position.x) + OffsetX;
            int posY = (int)math.round(transform.Position.z) + OffsetY;
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
        [NativeDisableContainerSafetyRestriction] // Since we are performing a lookup and modifying a component of the same type as a ref this tag is needed
        [ReadOnly] public ComponentLookup<PhysicsVelocity> VelocityLookup;
        [ReadOnly] public NativeArray<float2> FlowFieldDirection;
        [ReadOnly] public NativeArray<byte> CostField;
        public float SeparationRadius;
        public float SeparationWeight;
        public float AlignmentWeight;
        public float CohesionWeight;
        public float LookAheadTime; // How far do we look ahead in time for obstacles
        public float AvoidanceWeight; // How much weight do we apply to avoiding obstacles
        public int Width;
        public int Height;
        public int OffsetX;
        public int OffsetY;
        public float DeltaTime;
        public void Execute(Entity entity, ref PhysicsVelocity velocity, in LocalTransform transform)
        {
            int posX = (int)math.round(transform.Position.x) + OffsetX;
            int posY = (int)math.round(transform.Position.z) + OffsetY;
            if (posX < 0 || posX > Width - 1 || posY < 0 || posY > Height - 1)
                return;
            
            int currentCell = posY * Width + posX;
            
            float3 separationForce = float3.zero;
            float3 averageVelocity = float3.zero;
            float3 averagePosition = float3.zero;
            int neighborCount = 0;
            // Check 3 x 3 neighbors for distance and average velocity
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
                                
                                // For our average velocity
                                neighborCount += 1;
                                averageVelocity += VelocityLookup[neighbor].Linear;
                                
                                // Average position for cohesion
                                averagePosition += neighborPos;
                            }
                        } while (SpatialMap.TryGetNextValue(out neighbor, ref iterator));
                    }
                }
            }

            if (neighborCount > 0)
            {
                averageVelocity /= neighborCount;
                averagePosition /= neighborCount;
            }
            
            // Movement logic
            float MaxSpeed = 1f;
            float TurnSpeed = 2f;
            
            // Find our future position and apply a force away from any obstacles
            float3 futurePosition = transform.Position + (velocity.Linear * LookAheadTime);
            float3 avoidanceForce = float3.zero;
            int futureX = Mathf.RoundToInt(futurePosition.x) + OffsetX;
            int futureY = Mathf.RoundToInt(futurePosition.z) + OffsetY;
            if (futureX >= 0 && futureX < Width && futureY >= 0 && futureY < Height)
            {
                int futureCellIndex =  futureY * Width + futureX;

                float3 futureToCurrent = transform.Position - futurePosition;
                
                bool isFutureCellWalkable = CostField[futureCellIndex] != byte.MaxValue;
                if (!isFutureCellWalkable)
                {
                    avoidanceForce = math.normalizesafe(futureToCurrent);
                }
            }
            
            float3 alignmentDirection = math.normalizesafe(new float3(averageVelocity.x, 0, averageVelocity.z));
            float3 centerOfMass =  math.normalizesafe(new float3(averagePosition.x, 0, averagePosition.z) - transform.Position);
            float3 flowFieldDirection = new float3(FlowFieldDirection[currentCell].x, 0 , FlowFieldDirection[currentCell].y);
            float3 targetDirection = math.normalizesafe(flowFieldDirection + (separationForce * SeparationWeight) + 
                                                        (alignmentDirection *  AlignmentWeight) + (centerOfMass * CohesionWeight) + (avoidanceForce * AvoidanceWeight));
            
            
            targetDirection *= MaxSpeed;
            // velocity.Linear = new float3(targetDirection.x, velocity.Linear.y, targetDirection.z);
            velocity.Linear = math.lerp(velocity.Linear, new float3(targetDirection.x, velocity.Linear.y, targetDirection.z), TurnSpeed * DeltaTime);
            // Are we moving faster than we should?
            if(math.lengthsq(targetDirection) > math.square(MaxSpeed))
            {
                // Flatten the vectors to keep our vertical gravity
                float3 horizontalVelocityScaled = math.normalizesafe(new float3(targetDirection.x, 0, targetDirection.y)) * MaxSpeed;
                float3 newVelocity = new float3(horizontalVelocityScaled.x, velocity.Linear.y, horizontalVelocityScaled.z);
                velocity.Linear = velocity.Linear = math.lerp(velocity.Linear, newVelocity, TurnSpeed * DeltaTime);
            }
        }
    }

    public struct ClearSpatialMap : IJob
    {
        public NativeParallelMultiHashMap<int, Entity> SpatialMap;
        public void Execute()
        {
            SpatialMap.Clear();
        }
    }
}

/*  --------------------Notes---------------------
    When using hordes there is typically 3 parts to accomodate large hordes
    (1) Separation - If a neighbor (ex: left neighbor) is too close to the entity, we need to apply a vector in the opposite direction (right)
    (2) Alignment - Since the entity is assumed to move in "crowds", it would be optimal to steer towards an average vector of the entire crowd
    (3) Cohesion - If the entity drifts away from the crowd, we need to move towards the average position of the crowd (center of mass)
    ----------------------------------------------
*/