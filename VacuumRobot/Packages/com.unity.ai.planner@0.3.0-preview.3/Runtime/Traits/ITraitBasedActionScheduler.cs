using System;
using Unity.AI.Planner.Jobs;

namespace Unity.AI.Planner.Traits
{
    /// <summary>
    /// A specialized interface of <see cref="IActionScheduler{TStateKey,TStateData,TStateDataContext,TStateManager,TActionKey}"/>
    /// for trait-based domains
    /// </summary>
    /// <typeparam name="TObject">Object type</typeparam>
    /// <typeparam name="TStateKey">StateKey type</typeparam>
    /// <typeparam name="TStateData">StateData type</typeparam>
    /// <typeparam name="TStateDataContext">StateDataContext type</typeparam>
    /// <typeparam name="TStateManager">StateManager type</typeparam>
    /// <typeparam name="TActionKey">ActionKey type</typeparam>
    interface ITraitBasedActionScheduler<TObject, TStateKey, TStateData, TStateDataContext, TStateManager, TActionKey> :
        IActionScheduler<TStateKey, TStateData, TStateDataContext, TStateManager, TActionKey>
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TStateData : unmanaged, ITraitBasedStateData<TObject>
        where TStateDataContext : struct, ITraitBasedStateDataContext<TObject, TStateKey, TStateData>
        where TStateManager : ITraitBasedStateManager<TObject, TStateKey, TStateData, TStateDataContext>
        where TActionKey : unmanaged, IEquatable<TActionKey>, IActionKeyWithGuid
        where TObject : unmanaged, ITraitBasedObject
    {
    }
}
