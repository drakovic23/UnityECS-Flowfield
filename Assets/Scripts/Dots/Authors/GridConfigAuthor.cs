using Unity.Entities;
using UnityEngine;

public class GridConfigAuthor : MonoBehaviour
{
    public GameObject GridPrefab;
    [Header("Debug")]
    public bool ShowVisualGrid = true;
    class GridPrefabBaker : Baker<GridConfigAuthor>
    {
        public override void Bake(GridConfigAuthor authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            
            AddComponent(entity, new GridCellDebugConfig{
                GridPrefab = GetEntity(authoring.GridPrefab, TransformUsageFlags.Dynamic)
            });
            
            if(authoring.ShowVisualGrid)
                AddComponent(entity, new DebugGridTag());
            
            Debug.Log("Baked grid");
        }
    }
}
