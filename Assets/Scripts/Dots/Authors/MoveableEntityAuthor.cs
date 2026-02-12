using Unity.Entities;
using UnityEngine;

public class MoveableEntityAuthor : MonoBehaviour
{
    class MoveableEntityBaker : Baker<MoveableEntityAuthor>
    {
        public override void Bake(MoveableEntityAuthor authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new MoveableEntity());
        }
    }
}
