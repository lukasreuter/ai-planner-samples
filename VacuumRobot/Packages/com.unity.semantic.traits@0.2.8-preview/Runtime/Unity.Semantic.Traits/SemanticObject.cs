using Unity.Entities;
using UnityEngine;

namespace Unity.Semantic.Traits
{
    /// <summary>
    /// Component used on objects that contain Traits
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu(Constants.MenuName + "/Semantic Object")]
    public class SemanticObject : MonoBehaviour
    {
        [SerializeField]
        bool m_EnableTraitInspectors = true;

        /// <summary>
        /// Entity Manager
        /// </summary>
        public static EntityManager EntityManager => World.EntityManager;

        public static World World => World.DefaultGameObjectInjectionWorld;

        /// <summary>
        /// Parent object entity
        /// </summary>
        public Entity Entity { get; private set; }

        internal bool EnableTraitInspectors
        {
            get => m_EnableTraitInspectors;
            set => m_EnableTraitInspectors = value;
        }

        /// <summary>
        /// Convert SemanticObject component to a SemanticObjectData on this object entity
        /// </summary>
        public void Start()
        {
            if (GetComponent<RuntimeConvertTraits>() == null)
            {
                return;
            }

            var destinationManager = EntityManager;
            var semanticID = new SemanticID(gameObject);

            var result = SemanticIDSystem.FindSemanticID(semanticID, destinationManager);

            var entity = result == Entity.Null
                ? destinationManager.CreateEntity()
                : result;

            destinationManager.SetName(entity, $"{gameObject.name} Traits");

            Entity = entity;

            destinationManager.AddComponentData(entity, new SemanticID(gameObject));
            destinationManager.AddComponent<SemanticObjectData>(entity);
            destinationManager.AddComponentObject(entity, transform);
        }

        private sealed class Baker : Baker<SemanticObject>
        {
            public override void Bake(SemanticObject authoring)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                DependsOn(authoring.gameObject);
                AddComponent(e, new SemanticID
                {
                    instanceID = authoring.gameObject.GetInstanceID(),
                });
                AddComponent<SemanticObjectData>(e);
                AddComponentObject(e, GetComponent<Transform>());
            }
        }

        private void OnDestroy()
        {
            if (World is not { IsCreated: true })
            {
                return;
            }

            EntityManager.RemoveComponent<Transform>(Entity);
            EntityManager.RemoveComponent<SemanticObjectData>(Entity);
            EntityManager.RemoveComponent<SemanticID>(Entity);

            if (EntityManager.GetComponentCount(Entity) == 0)
            {
                EntityManager.DestroyEntity(Entity);
            }
        }
    }
}
