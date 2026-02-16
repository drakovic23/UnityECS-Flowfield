using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FlowFieldAuthor : MonoBehaviour
{
    public int GridWidth;
    public int GridHeight;
    [Header("Boids Settings")]
    [Range(0,5)] public float SeparationRadius = 1.5f;
    [Range(0,5)] public float SeparationWeight = 0.25f;
    [Range(0,5)] public float AlignmentWeight = 0.8f;
    [Range(0,5)] public float CohesionWeight = 0.2f;
    [Range(0,5)] public float LookAheadTime = 0.5f;
    [Range(0,5)] public float AvoidanceWeight = 0.5f;
    class FlowFieldBaker : Baker<FlowFieldAuthor>
    {
        public override void Bake(FlowFieldAuthor authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new GridSettings{
                GridSize = new int2(authoring.GridWidth, authoring.GridHeight),
                OffsetX = authoring.GridWidth / 2,
                OffsetY = authoring.GridHeight / 2,
                TotalSize = authoring.GridWidth * authoring.GridHeight
            });
            
            int gridSize = authoring.GridWidth * authoring.GridHeight;
            // We need to attach a dynamic buffer
            var buffer = AddBuffer<FlowFieldDirection>(entity);
            buffer.Resize(gridSize, NativeArrayOptions.ClearMemory);

            var costBuffer = AddBuffer<CostField>(entity);
            costBuffer.Resize(gridSize, NativeArrayOptions.ClearMemory);
            
            var integrationBuffer = AddBuffer<IntegrationField>(entity);
            integrationBuffer.Resize(gridSize, NativeArrayOptions.ClearMemory);
            
            var obstacleCountBuffer = AddBuffer<ObstacleCountMap>(entity);
            obstacleCountBuffer.Resize(gridSize, NativeArrayOptions.ClearMemory);
                
            AddComponent(entity, new MovementSettings{
                SeparationRadius = authoring.SeparationRadius,
                AlignmentWeight = authoring.AlignmentWeight,
                CohesionWeight = authoring.CohesionWeight,
                LookAheadTime = authoring.LookAheadTime,
                AvoidanceWeight = authoring.AvoidanceWeight,
                SeparationWeight = authoring.SeparationWeight,
            });
            
            // For the initial bake
            AddComponent(entity, new PerformCostFieldBake());
            AddComponent(entity, new PerformBakeTag());
            
            #if UNITY_EDITOR
                Debug.Log($"Baked grid with {authoring.GridWidth *  authoring.GridHeight} total cells");
                Debug.Log($"OffsetX: {authoring.GridWidth / 2}");
                Debug.Log($"OffsetY: {authoring.GridHeight / 2}");
            #endif
        }
    }
}
