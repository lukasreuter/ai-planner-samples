using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityObject = UnityEngine.Object;

[assembly: InternalsVisibleTo("Unity.Semantic.Traits")]

namespace Unity.Semantic.Traits
{
#if UNITY_EDITOR
    [CustomEditor(typeof(RuntimeConvertTraits))]
    internal class RuntimeConvertTraitsEditor : Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();

            root.Add(new HelpBox(
                "Add this component if this GameObject is not part of a SubScene. " +
                "If it is part of a SubScene this component is not necessary and should be removed.",
                HelpBoxMessageType.Info));

            return root;
        }
    }
#endif

    [DisallowMultipleComponent]
    [AddComponentMenu("DOTS/Runtime Convert Traits")]
    public class RuntimeConvertTraits : MonoBehaviour
    {
    }
}
