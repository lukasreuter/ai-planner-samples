using System;
using Unity.Entities;
using UnityEngine;

namespace Unity.Semantic.Traits
{
public struct SemanticID : IComponentData, IEquatable<SemanticID>
{
    public int instanceID;

    public SemanticID(GameObject gameObject)
    {
        instanceID = gameObject.GetInstanceID();
    }

    public bool Equals(SemanticID other) => instanceID == other.instanceID;

    public override bool Equals(object obj) => obj is SemanticID other && Equals(other);

    public override int GetHashCode() => instanceID;

    public static bool operator ==(SemanticID a, SemanticID b) => a.Equals(b);

    public static bool operator !=(SemanticID a, SemanticID b) => !a.Equals(b);
}
}
