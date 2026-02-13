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

public struct FlowFieldData : IComponentData
{
    public int2 GridSize;
}

public struct FlowFieldDirection : IBufferElementData
{
    public float2 Direction;
}

public struct CostField : IBufferElementData
{
    public byte MovementCost;
}

public struct ObstacleTag : IComponentData{}
public struct IntegrationField : IBufferElementData
{
    public ushort TotalMoveCost;
}

public struct MoveableEntity : IComponentData{}

// Used as an event for the initial bake
public struct PerformBakeTag : IComponentData{}

