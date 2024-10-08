using System;
using System.Data;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Unity.AI.Planner.Jobs
{
    [BurstCompile]
    struct EvaluateNewStatesJob<TStateKey, TStateData, TStateDataContext, TCumulativeRewardEstimator, TTerminationEvaluator> : IJobParallelForDefer
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TStateData : unmanaged
        where TStateDataContext : struct, IStateDataContext<TStateKey, TStateData>
        where TCumulativeRewardEstimator : unmanaged, ICumulativeRewardEstimator<TStateData>
        where TTerminationEvaluator : unmanaged, ITerminationEvaluator<TStateData>
    {
        // Input
        [ReadOnly] public TCumulativeRewardEstimator CumulativeRewardEstimator;
        [ReadOnly] public TTerminationEvaluator TerminationEvaluator;
        [ReadOnly] public TStateDataContext StateDataContext;
        [ReadOnly] public NativeArray<TStateKey> States;

        // Output
        [WriteOnly] public NativeParallelHashMap<TStateKey, StateInfo>.ParallelWriter StateInfoLookup;
        [WriteOnly] public NativeParallelMultiHashMap<int, TStateKey>.ParallelWriter BinnedStateKeys;

        public void Execute(int index)
        {
            var stateKey = States[index];
            var stateData = StateDataContext.GetStateData(stateKey);

            var terminal = TerminationEvaluator.IsTerminal(stateData, out var terminalReward);
            var value = terminal ?
                new BoundedValue(terminalReward, terminalReward, terminalReward) :
                CumulativeRewardEstimator.Evaluate(stateData);

            if (!terminal)
            {
                if (float.IsNaN(value.LowerBound) || float.IsNaN(value.Average) || float.IsNaN(value.UpperBound)
                || float.IsInfinity(value.LowerBound) || float.IsInfinity(value.Average) || float.IsInfinity(value.UpperBound))
                    throw new NotFiniteNumberException($"BoundedValue contains an invalid value; Please check reward estimation rules for {typeof(TCumulativeRewardEstimator)}");

                if (value.LowerBound > value.Average)
                    throw new ConstraintException($"Lower bound should not be greater than the average; Please check reward estimation rules for {typeof(TCumulativeRewardEstimator)}");

                if (value.UpperBound < value.Average)
                    throw new ConstraintException($"Upper bound should not be less than the average; Please check reward estimation rules for {typeof(TCumulativeRewardEstimator)}");

                if (value.LowerBound > value.UpperBound)
                    throw new ConstraintException($"Lower bound should not be greater than the upper bound; Please check reward estimation rules for {typeof(TCumulativeRewardEstimator)}");
            }

            StateInfoLookup.TryAdd(stateKey, new StateInfo
            {
                SubplanIsComplete = terminal,
                CumulativeRewardEstimate = value,
            });

            BinnedStateKeys.Add(stateKey.GetHashCode(), stateKey);
        }
    }
}
