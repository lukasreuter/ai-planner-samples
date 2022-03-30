using System;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Unity.AI.Planner.Jobs
{
    interface IDestroyStatesScheduler<TStateKey, TStateData, TStateDataContext, TStateManager>
        where TStateManager : IStateManager<TStateKey, TStateData, TStateDataContext>
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TStateData : unmanaged
        where TStateDataContext : struct, IStateDataContext<TStateKey, TStateData>
    {
        TStateManager StateManager {set;}
        NativeQueue<TStateKey> StatesToDestroy { set; }

        JobHandle Schedule(JobHandle inputDeps);
    }
}
