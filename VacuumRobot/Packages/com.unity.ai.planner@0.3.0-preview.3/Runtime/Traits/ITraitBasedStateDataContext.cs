﻿using System;

namespace Unity.AI.Planner.Traits
{
    /// <summary>
    /// A specialized interface of <see cref="IStateDataContext{TStateKey,TStateData}"/> for trait-based domains
    /// </summary>
    /// <typeparam name="TObject">Object type</typeparam>
    /// <typeparam name="TStateKey">StateKey type</typeparam>
    /// <typeparam name="TStateData">StateData type</typeparam>
    interface ITraitBasedStateDataContext<TObject, TStateKey, TStateData> : IStateDataContext<TStateKey, TStateData>
        where TObject : unmanaged, ITraitBasedObject
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TStateData : unmanaged, ITraitBasedStateData<TObject>
    {
    }
}
