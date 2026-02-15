using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class ObstacleAuthor : MonoBehaviour
{
    class ObstacleBaker : Baker<ObstacleAuthor>
    {
        public override void Bake(ObstacleAuthor authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Renderable); // Assuming obstacles are static objects
            
            // Calculate the grid bounds of the obstacle
            // floor() and ceil() may be better than round() for overlapping obstacles - needs testing
            int minCornerX = (int) math.floor(authoring.transform.position.x - (authoring.transform.localScale.x * 0.5f));
            int minCornerY = (int) math.floor(authoring.transform.position.z - (authoring.transform.localScale.z * 0.5f));
            int maxCornerX = (int) math.ceil(authoring.transform.position.x + (authoring.transform.localScale.x * 0.5f));
            int maxCornerY = (int) math.ceil(authoring.transform.position.z + (authoring.transform.localScale.z * 0.5f));
            
            AddComponent(entity, new ObstacleTag());
            AddComponent(entity, new ObstacleCleanup{
                MinGridBound = new int2(minCornerX, minCornerY),
                MaxGridBound = new int2(maxCornerX, maxCornerY)
            });
        }
    }
}
