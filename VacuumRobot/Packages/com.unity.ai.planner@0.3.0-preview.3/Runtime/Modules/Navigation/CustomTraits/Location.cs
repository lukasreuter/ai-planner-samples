using System;
using Unity.Semantic.Traits;
using Unity.Entities;
using UnityEngine;

namespace Generated.Semantic.Traits
{
    /// <summary>
    /// Component representing the Location trait.
    /// </summary>
    //[ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Semantic/Traits/Location (Trait)")]
    [RequireComponent(typeof(SemanticObject))]
    public partial class Location : MonoBehaviour, ITrait
    {
        /// <summary>
        /// The transform of the object.
        /// </summary>
        public UnityEngine.Transform Transform
        {
            get { return m_p338023941; }
            set
            {
                var em = SemanticObject.EntityManager;
                LocationData data = default;
                var dataActive = em.HasComponent<LocationData>(m_Entity);
                if (dataActive)
                    data = em.GetComponentData<LocationData>(m_Entity);
                m_p338023941 = value;
                data.Transform = transform;
                if (dataActive)
                    em.SetComponentData(m_Entity, data);

                Position = value.position;
                Forward = value.forward;
            }
        }

        /// <summary>
        /// The position of the object.
        /// </summary>
        public UnityEngine.Vector3 Position
        {
            get
            {
                var em = SemanticObject.EntityManager;
                if (em.HasComponent<LocationData>(m_Entity))
                {
                    m_p2084774077 = em.GetComponentData<LocationData>(m_Entity).Position;
                }

                return m_p2084774077;
            }
            set
            {
                var em = SemanticObject.EntityManager;
                LocationData data = default;
                var dataActive = em.HasComponent<LocationData>(m_Entity);
                if (dataActive)
                    data = em.GetComponentData<LocationData>(m_Entity);
                Transform.position = data.Position = m_p2084774077 = value;
                if (dataActive)
                    em.SetComponentData(m_Entity, data);
            }
        }

        /// <summary>
        /// The forward vector of the object.
        /// </summary>
        public UnityEngine.Vector3 Forward
        {
            get
            {
                var em = SemanticObject.EntityManager;
                if (em.HasComponent<LocationData>(m_Entity))
                {
                    m_p2006904664 = em.GetComponentData<LocationData>(m_Entity).Forward;
                }

                return m_p2006904664;
            }
            set
            {
                var em = SemanticObject.EntityManager;
                LocationData data = default;
                var dataActive = em.HasComponent<LocationData>(m_Entity);
                if (dataActive)
                    data = em.GetComponentData<LocationData>(m_Entity);
                Transform.forward = data.Forward = m_p2006904664 = value;
                if (dataActive)
                    em.SetComponentData(m_Entity, data);
            }
        }

        /// <summary>
        /// The component data representation of the trait.
        /// </summary>
        public LocationData Data
        {
            get => SemanticObject.World is { IsCreated: true } &&
                   SemanticObject.World.EntityManager.HasComponent<LocationData>(m_Entity)
                ? SemanticObject.World.EntityManager.GetComponentData<LocationData>(m_Entity)
                : GetData();
            set
            {
                if (SemanticObject.World is { IsCreated: true } &&
                    SemanticObject.World.EntityManager.HasComponent<LocationData>(m_Entity))
                {
                    SemanticObject.World.EntityManager.SetComponentData(m_Entity, value);
                }
            }
        }

#pragma warning disable 649
        [SerializeField]
        [InspectorName("Transform")]
        UnityEngine.Transform m_p338023941 = default;
        [SerializeField]
        [HideInInspector]
        UnityEngine.Vector3 m_p2084774077 = default;
        [SerializeField]
        [HideInInspector]
        UnityEngine.Vector3 m_p2006904664 = default;
#pragma warning restore 649

        Entity m_Entity;

        LocationData GetData()
        {
            LocationData data = default;
            data.Transform = m_p338023941;

            return data;
        }


        private void Start()
        {
#warning this throws all kinds of errors when play mode options are enabled
            if (GetComponent<RuntimeConvertTraits>() == null)
            {
                return;
            }

            // Handle the case where this trait is added after conversion
            var semanticObject = GetComponent<SemanticObject>();
            if (semanticObject && semanticObject.Entity != Entity.Null)
            {
                RuntimeConvert(semanticObject.Entity, SemanticObject.EntityManager);
            }

            Transform = gameObject.transform;
        }

        public void RuntimeConvert(Entity entity, EntityManager destinationManager)
        {
            m_Entity = entity;

            if (!destinationManager.HasComponent(entity, typeof(LocationData)))
            {
                destinationManager.AddComponentData(entity, GetData());
            }
        }

        /// <summary>
        /// Converts and assigns the monobehaviour trait component data to the entity representation.
        /// </summary>
        /// <param name="entity">The entity on which the trait data is to be assigned.</param>
        /// <param name="destinationManager">The entity manager for the given entity.</param>
        /// <param name="_">An unused GameObjectConversionSystem parameter, needed for IConvertGameObjectToEntity.</param>
        public void Convert(Entity entity, EntityManager destinationManager, GameObjectConversionSystem _)
        {
            destinationManager.AddComponentData(entity, GetData());
        }

        private void OnDestroy()
        {
            if (SemanticObject.World is { IsCreated: true })
            {
                var em = SemanticObject.EntityManager;
                em.RemoveComponent<LocationData>(m_Entity);
                if (em.GetComponentCount(m_Entity) == 0)
                {
                    em.DestroyEntity(m_Entity);
                }
            }
        }

        private void OnValidate()
        {

            // Commit local fields to backing store
            Data = GetData();
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            TraitGizmos.DrawGizmoForTrait(nameof(LocationData), gameObject, Data);
        }
#endif
    }
}
