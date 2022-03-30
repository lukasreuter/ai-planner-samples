using Unity.Entities;
using Unity.Semantic.Traits;
using UnityEngine;

namespace UnityEditor.Semantic.Traits.CodeGen
{
    class ObjectReferencePropertyDescriptor : TraitPropertyDescriptor<ObjectReferenceProperty>
    {
        public override TraitPropertyDescriptorData GetData(ObjectReferenceProperty property)
        {
            return new TraitPropertyDescriptorData(typeof(GameObject).ToString(), typeof(Entity).ToString(), null);
        }
    }
}
