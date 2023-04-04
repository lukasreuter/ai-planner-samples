using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Semantic.Traits;
using Unity.Entities;
using UnityEngine;

namespace Generated.Semantic.Traits
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Semantic/Traits/PlanningAgent (Trait)")]
    [RequireComponent(typeof(SemanticObject))]
    public partial class PlanningAgent : MonoBehaviour, ITrait
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

            if (!destinationManager.HasComponent(entity, typeof(PlanningAgentData)))
            {
                destinationManager.AddComponent<PlanningAgentData>(entity);
            }
        }

        private class Baker : Baker<PlanningAgent>
        {
            public override void Bake(PlanningAgent authoring)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<PlanningAgentData>(e);
            }
        }

        private void OnDestroy()
        {
            if (SemanticObject.World is { IsCreated: true })
            {
                var em = SemanticObject.EntityManager;
                em.RemoveComponent<PlanningAgentData>(m_Entity);
                if (em.GetComponentCount(m_Entity) == 0)
                {
                    em.DestroyEntity(m_Entity);
                }
            }
        }

        

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            TraitGizmos.DrawGizmoForTrait(nameof(PlanningAgentData), gameObject,null);
        }
#endif
    }
}
