using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;


// This is currently only used for debugging and gives a visual for the grid
// Nothing here calculates the df or ff.
public partial struct CreateGridSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        _hasCooked = false;
    }
    
    const int WIDTH = 40;
    const int HEIGHT = 40;
    bool _hasCooked;
    public void OnUpdate(ref SystemState state)
    {
        int count = 0;
        int cellCount = 0;
        
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        quaternion rotation = quaternion.Euler(math.radians(90), 0,0);
        
        foreach (var (gridDebugConfig, tag, entity) in SystemAPI.Query<RefRO<GridCellDebugConfig>, RefRO<DebugGridTag>>().WithEntityAccess())
        {
            if(count > 1)
            {
                Debug.LogError("Count > 1");
                return;
            }
            
            int offsetX = WIDTH / 2;
            int offsetY = HEIGHT / 2;
            for (int x = 0; x < WIDTH; x++)
            {
                for (int y = 0; y < HEIGHT; y++)
                {
                    Entity newCell = ecb.Instantiate(gridDebugConfig.ValueRO.GridPrefab);

                    int posX = x - offsetX;
                    int posY = y - offsetY;
                    ecb.SetComponent(newCell, LocalTransform.FromPositionRotation(new float3(posX, 0.1f, posY), rotation));
                    cellCount += 1;
                }
            }
            
            // Remove the tag so we don't do this multiple times
            ecb.RemoveComponent<DebugGridTag>(entity);
            count += 1;
        }
        
        if(!_hasCooked)
        {
            Debug.Log("Created " + cellCount + " grid cells");
            _hasCooked = true;
        }
    }
}
