using Unity.Entities;
using UnityEngine;

// Not currently used - should be removed
public class TargetAuthor : MonoBehaviour
{
    class TargetBaker : Baker<TargetAuthor>
    {
        public override void Bake(TargetAuthor authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new PlayerTag());
        }
    }
}
