﻿using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.AI.Planner.Jobs
{
    [BurstCompile]
    struct PrepareForExpansionJob<TStateKey, TActionKey> : IJob
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TActionKey : unmanaged, IEquatable<TActionKey>
    {
        public PlanGraph<TStateKey, StateInfo, TActionKey, ActionInfo, StateTransitionInfo> planGraph { get; set; }
        public NativeParallelMultiHashMap<int, TStateKey> BinnedStateKeys;

        public NativeQueue<StateTransitionInfoPair<TStateKey, TActionKey, StateTransitionInfo>> InputStateExpansionInfo { get; set; }
        public NativeList<StateTransitionInfoPair<TStateKey, TActionKey, StateTransitionInfo>> OutputStateExpansionInfo { get; set; }

        public void Execute()
        {
            var capacityNeeded = InputStateExpansionInfo.Count;
            planGraph.ExpandBy(capacityNeeded, capacityNeeded);

            if (BinnedStateKeys.Count() + capacityNeeded > BinnedStateKeys.Capacity)
                BinnedStateKeys.Capacity = Math.Max(BinnedStateKeys.Count() + capacityNeeded, BinnedStateKeys.Capacity * 2);

            while (InputStateExpansionInfo.TryDequeue(out var item))
            {
                OutputStateExpansionInfo.Add(item);
            }
        }
    }

    [BurstCompile]
    struct GraphExpansionJob<TStateKey, TStateData, TStateDataContext, TActionKey> : IJobParallelForDefer
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TStateData : unmanaged
        where TStateDataContext : struct, IStateDataContext<TStateKey, TStateData>
        where TActionKey : unmanaged, IEquatable<TActionKey>
    {
        [ReadOnly] public NativeArray<StateTransitionInfoPair<TStateKey, TActionKey, StateTransitionInfo>> NewStateTransitionInfoPairs;
        [ReadOnly] public NativeParallelMultiHashMap<int, TStateKey> BinnedStateKeys;

        public TStateDataContext StateDataContext;

        public NativeQueue<TStateKey>.ParallelWriter NewStates;
        public NativeQueue<TStateKey>.ParallelWriter StatesToDestroy;

        public NativeParallelMultiHashMap<TStateKey, TActionKey>.ParallelWriter ActionLookup;
        public NativeParallelMultiHashMap<StateActionPair<TStateKey, TActionKey>, TStateKey>.ParallelWriter ResultingStateLookup;
        public NativeParallelMultiHashMap<TStateKey, TStateKey>.ParallelWriter PredecessorGraph;

        public NativeParallelHashMap<StateActionPair<TStateKey, TActionKey>, ActionInfo>.ParallelWriter ActionInfoLookup;
        public NativeParallelHashMap<StateTransition<TStateKey, TActionKey>, StateTransitionInfo>.ParallelWriter StateTransitionInfoLookup;

        public void Execute(int index)
        {
            var stateTransitionInfoPair = NewStateTransitionInfoPairs[index];
            var stateTransition = stateTransitionInfoPair.StateTransition;
            var stateTransitionInfo = stateTransitionInfoPair.StateTransitionInfo;

            var precedingStateKey = stateTransition.PredecessorStateKey;
            var actionKey = stateTransition.ActionKey;
            var stateKey = stateTransition.SuccessorStateKey;
            var stateHashCode = stateKey.GetHashCode();

            var stateData = StateDataContext.GetStateData(stateKey);

            // Iterate over all potential matches. If any match -> existing; otherwise -> new.
            TStateKey otherStateKey;
            if (BinnedStateKeys.TryGetFirstValue(stateHashCode, out otherStateKey, out var iterator))
            {
                do
                {
                    if (stateKey.GetHashCode() == otherStateKey.GetHashCode())
                    {
                        if (StateDataContext.Equals(stateData, StateDataContext.GetStateData(otherStateKey)))
                        {
                            WriteEdgeToState(precedingStateKey, actionKey, stateTransitionInfo, otherStateKey);
                            StatesToDestroy.Enqueue(stateKey);
                            return;
                        }
                    }
                } while (BinnedStateKeys.TryGetNextValue(out otherStateKey, ref iterator));
            }

            for (var i = 0; i < NewStateTransitionInfoPairs.Length; i++)
            {
                otherStateKey = NewStateTransitionInfoPairs[i].StateTransition.SuccessorStateKey;

                if (stateKey.GetHashCode() == otherStateKey.GetHashCode())
                {
                    if (StateDataContext.Equals(stateData, StateDataContext.GetStateData(otherStateKey)))
                    {
                        WriteEdgeToState(precedingStateKey, actionKey, stateTransitionInfo, otherStateKey);

                        if (i == index) // Matched to self -> output for reward estimation evaluation
                            NewStates.Enqueue(stateKey);
                        else
                            StatesToDestroy.Enqueue(stateKey);

                        return;
                    }
                }
            }
        }

        void WriteEdgeToState(TStateKey precedingStateKey, TActionKey actionKey, StateTransitionInfo stateTransitionInfo, TStateKey resultingStateKey)
        {
            var stateActionPair = new StateActionPair<TStateKey, TActionKey>(precedingStateKey, actionKey);
            ActionLookup.Add(precedingStateKey, actionKey);
            ActionInfoLookup.TryAdd(stateActionPair, default);
            ResultingStateLookup.Add(stateActionPair, resultingStateKey);
            StateTransitionInfoLookup.TryAdd(new StateTransition<TStateKey, TActionKey>(stateActionPair, resultingStateKey), stateTransitionInfo);
            PredecessorGraph.Add(resultingStateKey, precedingStateKey);
        }
    }
}
