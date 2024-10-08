using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Semantic.Traits;
using Unity.Entities;
using UnityEngine;

namespace Generated.Semantic.Traits
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Semantic/Traits/Moveable (Trait)")]
    [RequireComponent(typeof(SemanticObject))]
    public partial class Moveable : MonoBehaviour, ITrait
    {

        Entity m_Entity;

        
        private void Start()
        {
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
        }

        public void RuntimeConvert(Entity entity, EntityManager destinationManager)
        {
            m_Entity = entity;

            if (!destinationManager.HasComponent(entity, typeof(MoveableData)))
            {
                destinationManager.AddComponent<MoveableData>(entity);
            }
        }

        private class Baker : Baker<Moveable>
        {
            public override void Bake(Moveable authoring)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<MoveableData>(e);
            }
        }

        private void OnDestroy()
        {
            if (SemanticObject.World is { IsCreated: true })
            {
                var em = SemanticObject.EntityManager;
                em.RemoveComponent<MoveableData>(m_Entity);
                if (em.GetComponentCount(m_Entity) == 0)
                {
                    em.DestroyEntity(m_Entity);
                }
            }
        }

        

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            TraitGizmos.DrawGizmoForTrait(nameof(MoveableData), gameObject,null);
        }
#endif
    }
}
