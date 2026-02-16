using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct GridCellDebugConfig : IComponentData
{
    public Entity GridPrefab;
}

public struct DistanceFieldComponent : IComponentData
{
    public NativeArray<float2> DistanceField;
}

public struct DebugGridTag : IComponentData { }
public struct BakeGridEventTag : IComponentData {}

// This should be baked into any object so we can detect "it" when baking the grid

public struct PlayerTag : IComponentData
{
    public float3 Position;
}

public struct GridSettings : IComponentData
{
    public int2 GridSize;
    public int OffsetX;
    public int OffsetY;
    public int TotalSize;
}

public struct MovementSettings : IComponentData
{
    public float SeparationRadius;
    public float SeparationWeight;
    public float AlignmentWeight;
    public float CohesionWeight;
    public float LookAheadTime;
    public float AvoidanceWeight;
}

public struct FlowFieldDirection : IBufferElementData
{
    public float2 Direction;
}

public struct CostField : IBufferElementData
{
    public byte MovementCost;
}
public struct PerformCostFieldBake : IComponentData{}
public struct CostFieldReadyTag : IComponentData{}

public struct ObstacleTag : IComponentData { } // Acts as a "heartbeat" for the obstacle, can add other fields like health here
public struct ObstacleCleanup : ICleanupComponentData // This is passed when an entity is destroyed
{
    // We need Position and LocalScale to set the CostField values to a "walkable" value
    public int2 MinGridBound;
    public int2 MaxGridBound;
}

public struct ObstacleCountMap : IBufferElementData
{
    public byte ObstacleCount;
}
public struct IntegrationField : IBufferElementData
{
    public ushort TotalMoveCost;
}

public struct MoveableEntity : IComponentData{}

// Used as an event for the initial bake
public struct PerformBakeTag : IComponentData{}

public struct SpatialMap : IComponentData
{
    public NativeParallelMultiHashMap<int, Entity> OccupiedCells;
}