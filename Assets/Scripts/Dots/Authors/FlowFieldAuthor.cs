using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FlowFieldAuthor : MonoBehaviour
{
    public int GridWidth;
    public int GridHeight;
    
    class FlowFieldBaker : Baker<FlowFieldAuthor>
    {
        public override void Bake(FlowFieldAuthor authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new FlowFieldData{
                GridSize = new int2(authoring.GridWidth, authoring.GridHeight)
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
            
            // For the initial bake
            AddComponent(entity, new PerformBakeTag());
        }
    }
}
