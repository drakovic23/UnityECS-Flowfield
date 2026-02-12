using Unity.Entities;
using UnityEngine;

public class TargetEcsBridge : MonoBehaviour
{
    EntityManager _entityManager;
    Entity _playerEntity;

    Vector3 _lastPos;

    void Awake()
    {
        _lastPos = transform.position;
    }
    void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _playerEntity = _entityManager.CreateEntity(typeof(PlayerTag));
    }

    float _moveDistanceThreshold = 1f;
    void Update()
    {
        _entityManager.SetComponentData(_playerEntity, new PlayerTag { Position = transform.position });
        
        if (Vector3.Distance(transform.position, _lastPos) > _moveDistanceThreshold)
        {
            _entityManager.CreateEntity(typeof(PerformBakeTag));
            _lastPos = transform.position;
            Debug.Log("Created bake tag");
        }
    }
}
