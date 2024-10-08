﻿using System;

namespace Unity.AI.Planner.Traits
{
    /// <summary>
    /// A specialized interface of <see cref="IStateManager{TStateKey,TStateData,TStateDataContext}"/> for trait-based domains
    /// </summary>
    /// <typeparam name="TObject">Object type</typeparam>
    /// <typeparam name="TStateKey">StateKey type</typeparam>
    /// <typeparam name="TStateData">StateData type</typeparam>
    /// <typeparam name="TStateDataContext">StateDataContext type</typeparam>
    interface ITraitBasedStateManager<TObject, TStateKey, TStateData, TStateDataContext> : IStateManager<TStateKey, TStateData, TStateDataContext>
        where TObject : unmanaged, ITraitBasedObject
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TStateData : unmanaged, ITraitBasedStateData<TObject>
        where TStateDataContext : struct, ITraitBasedStateDataContext<TObject, TStateKey, TStateData>
    {
    }
}
