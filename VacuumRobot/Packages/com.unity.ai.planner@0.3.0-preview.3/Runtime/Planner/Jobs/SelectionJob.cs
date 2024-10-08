using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.AI.Planner.Jobs
{
    /// <summary>
    /// Modes designating the strategy for selecting state nodes to expand in the plan.
    /// </summary>
    public enum SelectionJobMode
    {
        /// <summary>
        /// All budgeted state nodes will be selected in a job by a single worker.
        /// </summary>
        Sequential,

        /// <summary>
        /// Budgeted state nodes will be selected in a parallel job by multiple workers.
        /// </summary>
        Parallel
    }

    [BurstCompile]
    struct SelectionJob<TStateKey, TActionKey> : IJob
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TActionKey : unmanaged, IEquatable<TActionKey>
    {
        public NativeParallelHashMap<TStateKey, int> StateDepthLookup;

        [ReadOnly] public int StateExpansionBudget;
        [ReadOnly] public NativeParallelHashMap<TStateKey, StateInfo> StateInfoLookup;
        [ReadOnly] public NativeParallelHashMap<StateActionPair<TStateKey, TActionKey>, ActionInfo> ActionInfoLookup;
        [ReadOnly] public TStateKey RootStateKey;
        [ReadOnly] public NativeParallelMultiHashMap<TStateKey, TActionKey> ActionLookup;
        [ReadOnly] public NativeParallelMultiHashMap<StateActionPair<TStateKey, TActionKey>, TStateKey> ResultingStateLookup;
        [ReadOnly] public NativeParallelHashMap<StateTransition<TStateKey, TActionKey>, StateTransitionInfo> StateTransitionInfoLookup;

        [WriteOnly] public NativeParallelMultiHashMap<TStateKey, int> AllSelectedStates;
        [WriteOnly] public NativeList<TStateKey> SelectedUnexpandedStates;


        struct HorizonExpandedInfo
        {
            public int Horizon;
            public bool Expanded;
        }

        struct StateHorizonPair : IEquatable<StateHorizonPair>
        {
            public TStateKey StateKey;
            public int Horizon;

            public bool Equals(StateHorizonPair other)
            {
                return Horizon == other.Horizon && StateKey.Equals(other.StateKey);
            }

            public override int GetHashCode()
            {
                return (StateKey.GetHashCode() * 397) ^ Horizon;
            }
        }

        struct StateActionPairUpperBoundInfo : IComparable<StateActionPairUpperBoundInfo>
        {
            public StateActionPair<TStateKey, TActionKey> StateActionPair;
            public float UpperBound;

            public int CompareTo(StateActionPairUpperBoundInfo other)
            {
                return UpperBound.CompareTo(other.UpperBound);
            }
        }

        struct StateBoundsRange : IComparable<StateBoundsRange>
        {
            public TStateKey StateKey;
            public float WeightedBoundsRange;

            public int CompareTo(StateBoundsRange other)
            {
                return WeightedBoundsRange.CompareTo(other.WeightedBoundsRange);
            }
        }

        struct SelectionJobData
        {
            public NativeParallelHashMap<TStateKey, HorizonExpandedInfo> SelectedStates;
            public NativeList<StateActionPairUpperBoundInfo> NonDominatedActions;
            public NativeList<StateBoundsRange> WeightedBoundsRanges;
            public NativeParallelHashMap<StateHorizonPair, int> StateHorizonBudgets;
            public NativeParallelMultiHashMap<int, TStateKey> QueuedStatesByHorizon;
            public int MaxHorizon;

            public SelectionJobData(int size)
            {
                SelectedStates = new NativeParallelHashMap<TStateKey, HorizonExpandedInfo>(size, Allocator.Temp);
                NonDominatedActions = new NativeList<StateActionPairUpperBoundInfo>(size, Allocator.Temp);
                WeightedBoundsRanges = new NativeList<StateBoundsRange>(size, Allocator.Temp);
                StateHorizonBudgets = new NativeParallelHashMap<StateHorizonPair, int>(size, Allocator.Temp);
                QueuedStatesByHorizon = new NativeParallelMultiHashMap<int, TStateKey>(size, Allocator.Temp);
                MaxHorizon = int.MinValue;
            }
        }

        public void Execute()
        {
            if (StateInfoLookup[RootStateKey].SubplanIsComplete) // Completed root; no need to expand.
                return;

            // Working space containers
            var data = new SelectionJobData(StateExpansionBudget);
            AssignBudgetToResultingState(RootStateKey, 0, StateExpansionBudget, ref data);

            // Walk graph until traversals terminate
            var queuedStates = data.QueuedStatesByHorizon;
            int horizon = data.MaxHorizon;
            while (horizon <= data.MaxHorizon)
            {
                var nextHorizon = horizon + 1;
                queuedStates.TryGetFirstValue(horizon, out var stateKey, out var iterator);
                do
                {
                    // Possible to have already processed the state/horizon
                    var stateHorizonPair = new StateHorizonPair {Horizon = horizon, StateKey = stateKey};
                    if (!data.StateHorizonBudgets.TryGetValue(stateHorizonPair, out var budget))
                        continue;

                    data.StateHorizonBudgets.Remove(stateHorizonPair);

                    // Assign budget to actions given maximum lower bound (prune dominated actions)
                    GetActionWithMaxLowerBound(stateKey, out _, out var maxLowerBound);
                    AssignBudgetToAllActions(stateKey, maxLowerBound, nextHorizon, budget, ref data);

                } while (queuedStates.TryGetNextValue(out stateKey, ref iterator));

                queuedStates.Remove(horizon);
                horizon++;
            }

            // Write to output containers.
            var selectedStateKeys = data.SelectedStates.GetKeyArray(Allocator.Temp);
            var selectedStateData = data.SelectedStates.GetValueArray(Allocator.Temp);
            for (int i = 0; i < selectedStateKeys.Length; i++)
            {
                var stateKey = selectedStateKeys[i];
                var selectedHorizonExpandedInfo = selectedStateData[i];
                var depth  = selectedHorizonExpandedInfo.Horizon;
                var expanded = selectedHorizonExpandedInfo.Expanded;

                StateDepthLookup.TryGetValue(stateKey, out var previousDepth);
                StateDepthLookup[stateKey] = math.max(depth, previousDepth);
                AllSelectedStates.Add(stateKey, depth);

                if (!expanded)
                    SelectedUnexpandedStates.Add(stateKey);
            }
        }

        void GetActionWithMaxLowerBound(TStateKey stateKey, out StateActionPair<TStateKey, TActionKey> maxLowerBoundPair, out float maxLowerBound)
        {
            ActionLookup.TryGetFirstValue(stateKey, out var actionKey, out var iterator);
            maxLowerBoundPair = default;
            maxLowerBound = float.MinValue;

            do
            {
                var stateActionPair = new StateActionPair<TStateKey, TActionKey>(stateKey, actionKey);
                var actionLowerBound = ActionInfoLookup[stateActionPair].CumulativeRewardEstimate.LowerBound;
                if (actionLowerBound > maxLowerBound)
                {
                    maxLowerBoundPair = stateActionPair;
                    maxLowerBound = actionLowerBound;
                }

            } while (ActionLookup.TryGetNextValue(out actionKey, ref iterator));
        }

        void AssignBudgetToAllActions(TStateKey stateKey, float maxLowerBound, int horizon, int budget, ref SelectionJobData data)
        {
            // Find non-dominated actions and max avg action
            StateActionPair<TStateKey, TActionKey> maxAveragePair = default;
            var maxAverage = float.MinValue;
            var nonDominatedActions = data.NonDominatedActions;
            nonDominatedActions.Clear();

            ActionLookup.TryGetFirstValue(stateKey, out var actionKey, out var iterator);
            do
            {
                var stateActionPair = new StateActionPair<TStateKey, TActionKey>(stateKey, actionKey);
                var actionInfo = ActionInfoLookup[stateActionPair];
                if (actionInfo.SubplanIsComplete) // skip complete subgraphs
                    continue;

                var cumulativeReward = actionInfo.CumulativeRewardEstimate;
                if (cumulativeReward.Average > maxAverage)
                {
                    maxAveragePair = stateActionPair;
                    maxAverage = cumulativeReward.Average;
                }

                if (cumulativeReward.UpperBound >= maxLowerBound)
                    nonDominatedActions.Add(new StateActionPairUpperBoundInfo {StateActionPair = stateActionPair, UpperBound = actionInfo.CumulativeRewardEstimate.UpperBound} );

            } while (ActionLookup.TryGetNextValue(out actionKey, ref iterator));

            // For non-dominated actions, add the exploration budget.
            nonDominatedActions.Sort();
            for (int i = nonDominatedActions.Length - 1; i >= 0; i--)
            {
                AssignBudgetToSingleAction(nonDominatedActions[i].StateActionPair, horizon, 1, ref data);

                budget--;
                if (budget == 0)
                    return;
            }

            // Assign exploitation budget
            AssignBudgetToSingleAction(maxAveragePair, horizon, budget, ref data);
        }

        void AssignBudgetToSingleAction(StateActionPair<TStateKey, TActionKey> stateActionPair, int horizon, int budget, ref SelectionJobData data)
        {
            var weightedBoundsRanges = data.WeightedBoundsRanges;
            weightedBoundsRanges.Clear();

            ResultingStateLookup.TryGetFirstValue(stateActionPair, out var resultingState, out var iterator);
            do
            {
                var resultingStateInfo = StateInfoLookup[resultingState];
                if (resultingStateInfo.SubplanIsComplete) // skip complete subgraphs
                    continue;

                var stateTransitionInfo = StateTransitionInfoLookup[new StateTransition<TStateKey, TActionKey>(stateActionPair, resultingState)];

                weightedBoundsRanges.Add(new StateBoundsRange
                {
                    StateKey = resultingState,
                    WeightedBoundsRange = stateTransitionInfo.Probability * resultingStateInfo.CumulativeRewardEstimate.Range
                });
            } while (ResultingStateLookup.TryGetNextValue(out resultingState, ref iterator));

            weightedBoundsRanges.Sort();
            for (int i = weightedBoundsRanges.Length - 1; i >= 0; i--)
            {
                AssignBudgetToResultingState(weightedBoundsRanges[i].StateKey, horizon, 1, ref data);

                budget -= 1;

                if (budget <= 0)
                {
                    return;
                }
            }

            if (weightedBoundsRanges.Length > 0)
            {
                AssignBudgetToResultingState(weightedBoundsRanges[0].StateKey, horizon, budget, ref data);
            }
        }

        void AssignBudgetToResultingState(TStateKey state, int horizon, int budget, ref SelectionJobData data)
        {
            // Check for termination of walk
            var expanded = ActionLookup.TryGetFirstValue(state, out _, out _);
            var inDepthMap = StateDepthLookup.TryGetValue(state, out var maxDepth);
            if (!expanded || !inDepthMap || horizon > maxDepth)
            {
                // Could have already been added at a greater depth from another branch.
                if (data.SelectedStates.TryGetValue(state, out var horizonExpandedPair) && horizonExpandedPair.Horizon >= horizon)
                    return;

                data.SelectedStates[state] = new HorizonExpandedInfo { Horizon = horizon, Expanded = expanded };
                return;
            }

            // otherwise, add to queue and budget lookup
            var stateHorizonPair = new StateHorizonPair { StateKey = state, Horizon = horizon };
            data.QueuedStatesByHorizon.Add(horizon, state);
            data.MaxHorizon = math.max(data.MaxHorizon, horizon);
            data.StateHorizonBudgets.TryGetValue(stateHorizonPair, out var existingBudget);
            data.StateHorizonBudgets[stateHorizonPair] = existingBudget + budget;
        }
    }

    [BurstCompile]
    struct SetupParallelSelectionJob<TStateKey> : IJob
        where TStateKey : unmanaged, IEquatable<TStateKey>
    {
        [ReadOnly] public TStateKey RootStateKey;
        [ReadOnly] public int Budget;
        [ReadOnly] public NativeParallelHashMap<TStateKey, StateInfo> StateInfoLookup;

        [WriteOnly] public NativeList<TStateKey> SelectionInputStates;
        [WriteOnly] public NativeList<int>  SelectionInputBudgets;
        public NativeParallelMultiHashMap<TStateKey, int> OutputStateBudgets;
        public NativeParallelHashMap<TStateKey, byte> SelectedUnexpandedStates;
        public NativeParallelMultiHashMap<TStateKey, int> AllSelectedStates;

        public void Execute()
        {
            SelectionInputStates.Add(RootStateKey);
            SelectionInputBudgets.Add(Budget);

            // Resize container to avoid full hash map
            int size = math.min(Budget, StateInfoLookup.Count());
            OutputStateBudgets.Capacity = math.max(OutputStateBudgets.Capacity, size);
            SelectedUnexpandedStates.Capacity = math.max(SelectedUnexpandedStates.Capacity, size);
            AllSelectedStates.Capacity = math.max(AllSelectedStates.Capacity, size);
        }
    }

    [BurstCompile]
    struct ParallelSelectionJob<TStateKey, TActionKey> : IJobParallelForDefer
        where TStateKey : unmanaged, IEquatable<TStateKey>
        where TActionKey : unmanaged, IEquatable<TActionKey>
    {
        // Needed data
        [ReadOnly] public NativeParallelHashMap<TStateKey, int> StateDepthLookup;
        [ReadOnly] public NativeParallelHashMap<TStateKey, StateInfo> StateInfoLookup;
        [ReadOnly] public NativeParallelHashMap<StateActionPair<TStateKey, TActionKey>, ActionInfo> ActionInfoLookup;
        [ReadOnly] public NativeParallelMultiHashMap<TStateKey, TActionKey> ActionLookup;
        [ReadOnly] public NativeParallelMultiHashMap<StateActionPair<TStateKey, TActionKey>, TStateKey> ResultingStateLookup;
        [ReadOnly] public NativeParallelHashMap<StateTransition<TStateKey, TActionKey>, StateTransitionInfo> StateTransitionInfoLookup;

        // Inputs
        [ReadOnly] public int Horizon;
        [ReadOnly] public NativeArray<TStateKey> InputStates;
        [ReadOnly] public NativeArray<int> InputBudgets;

        // Outputs
        [WriteOnly] public NativeParallelMultiHashMap<TStateKey, int>.ParallelWriter OutputStateBudgets;
        [WriteOnly] public NativeParallelMultiHashMap<TStateKey, int>.ParallelWriter SelectedStateHorizons;
        [WriteOnly] public NativeParallelHashMap<TStateKey, byte>.ParallelWriter SelectedUnexpandedStates;

        // Local containers
        [NativeDisableContainerSafetyRestriction] NativeList<StateActionPairUpperBoundInfo> m_NonDominatedActions;
        [NativeDisableContainerSafetyRestriction] NativeList<WeightedStateBoundsRange> m_StateBoundsRanges;


        struct StateActionPairUpperBoundInfo : IComparable<StateActionPairUpperBoundInfo>
        {
            public StateActionPair<TStateKey, TActionKey> StateActionPair;
            public float UpperBound;

            public int CompareTo(StateActionPairUpperBoundInfo other)
            {
                return other.UpperBound.CompareTo(UpperBound); // descending order
            }
        }

        struct WeightedStateBoundsRange : IComparable<WeightedStateBoundsRange>
        {
            public TStateKey StateKey;
            public float WeightedRange;

            public int CompareTo(WeightedStateBoundsRange other)
            {
                return other.WeightedRange.CompareTo(WeightedRange); // descending order
            }
        }


        public void Execute(int jobIndex)
        {
            var stateKey = InputStates[jobIndex];
            var budget = InputBudgets[jobIndex];

            // If not expanded, assign as selected and return
            if (!ActionLookup.ContainsKey(stateKey))
            {
                SelectedStateHorizons.Add(stateKey, Horizon);
                SelectedUnexpandedStates.TryAdd(stateKey, default);
                return;
            }

            // Find the max lower bound of an action and max average action
            float maxLowerBound = float.MinValue;
            float maxAverage = float.MinValue;
            var maxAverageAction = default(StateActionPair<TStateKey, TActionKey>);
            ActionLookup.TryGetFirstValue(stateKey, out var actionKey, out var iterator);
            do
            {
                var stateActionPair = new StateActionPair<TStateKey, TActionKey>(stateKey, actionKey);
                var actionInfo = ActionInfoLookup[stateActionPair];
                var cumulativeReward = actionInfo.CumulativeRewardEstimate;

                // Find max lower bound
                maxLowerBound = math.max(maxLowerBound, cumulativeReward.LowerBound);

                // Find best incomplete action
                if (!actionInfo.SubplanIsComplete && cumulativeReward.Average > maxAverage)
                {
                    maxAverageAction = stateActionPair;
                    maxAverage = cumulativeReward.Average;
                }
            } while (ActionLookup.TryGetNextValue(out actionKey, ref iterator));

            // Assign budget to actions given maximum lower bound (prune dominated actions)
            AssignBudgetToActions(stateKey, budget, maxAverageAction, maxLowerBound);
        }

        void AssignBudgetToActions(TStateKey stateKey, int budget, StateActionPair<TStateKey, TActionKey> maxAverageAction, float maxLowerBound)
        {
            // Containers for comparisons
            if (!m_NonDominatedActions.IsCreated)
            {
                m_NonDominatedActions = new NativeList<StateActionPairUpperBoundInfo>(ActionLookup.CountValuesForKey(stateKey), Allocator.Temp);
                m_StateBoundsRanges = new NativeList<WeightedStateBoundsRange>(16, Allocator.Temp);
            }
            else
            {
                m_NonDominatedActions.Clear();
                m_StateBoundsRanges.Clear();
            }

            // Find non-dominated actions and max avg action
            ActionLookup.TryGetFirstValue(stateKey, out var actionKey, out var iterator);
            do
            {
                var stateActionPair = new StateActionPair<TStateKey, TActionKey>(stateKey, actionKey);
                var actionInfo = ActionInfoLookup[stateActionPair];
                var cumulativeReward = actionInfo.CumulativeRewardEstimate;

                // Skip complete subgraphs and dominated subgraphs
                if (!actionInfo.SubplanIsComplete && cumulativeReward.UpperBound >= maxLowerBound)
                    m_NonDominatedActions.Add(new StateActionPairUpperBoundInfo { StateActionPair = stateActionPair, UpperBound = cumulativeReward.UpperBound });

            } while (ActionLookup.TryGetNextValue(out actionKey, ref iterator));

            // For non-dominated actions, add the exploration budget.
            m_NonDominatedActions.Sort();
            var toAssignOne = math.min(budget, m_NonDominatedActions.Length);
            for (int i = 0; i < toAssignOne; i++)
            {
                var stateActionPair = m_NonDominatedActions[i].StateActionPair;
                AssignBudgetToResultingStates(stateActionPair, stateActionPair.Equals(maxAverageAction) ? budget - toAssignOne + 1 : 1, m_StateBoundsRanges);
            }
        }

        void AssignBudgetToResultingStates(StateActionPair<TStateKey, TActionKey> stateActionPair, int budget, NativeList<WeightedStateBoundsRange> weightedRanges)
        {
            weightedRanges.Clear();

            ResultingStateLookup.TryGetFirstValue(stateActionPair, out var resultingState, out var iterator);
            do
            {
                var resultingStateInfo = StateInfoLookup[resultingState];
                // skip complete subgraphs
                if (!resultingStateInfo.SubplanIsComplete)
                {
                    var stateTransitionInfo = StateTransitionInfoLookup[new StateTransition<TStateKey, TActionKey>(stateActionPair, resultingState)];
                    weightedRanges.Add(new WeightedStateBoundsRange
                    {
                        StateKey = resultingState,
                        WeightedRange = stateTransitionInfo.Probability * resultingStateInfo.CumulativeRewardEstimate.Range,
                    });
                }

            } while (ResultingStateLookup.TryGetNextValue(out resultingState, ref iterator));

            weightedRanges.Sort();
            var toAssignOne = math.min(budget, weightedRanges.Length);
            AssignBudgetToState(weightedRanges[0].StateKey, budget - toAssignOne + 1);
            for (int i = 1; i < toAssignOne; i++)
            {
                AssignBudgetToState(weightedRanges[i].StateKey, 1);
            }
        }

        void AssignBudgetToState(TStateKey stateKey, int budget)
        {
            if (!ActionLookup.ContainsKey(stateKey))
            {
                // State is not expanded
                SelectedUnexpandedStates.TryAdd(stateKey, default);
                SelectedStateHorizons.Add(stateKey, Horizon + 1);
            }
            else if (!StateDepthLookup.TryGetValue(stateKey, out var maxDepth) || Horizon + 1 > maxDepth)
            {
                // State is reached at a deeper horizon than previously seen.
                SelectedStateHorizons.Add(stateKey, Horizon + 1);
            }
            else
            {
                // Otherwise, add to queue and budget lookup.
                OutputStateBudgets.Add(stateKey, budget);
            }
        }
    }

    [BurstCompile]
    struct CollectAndAssignSelectionBudgets<TStateKey> : IJob
        where TStateKey : unmanaged, IEquatable<TStateKey>
    {
        // Inputs
        public NativeParallelMultiHashMap<TStateKey, int> InputStateBudgets;

        // Outputs
        [WriteOnly] public NativeList<TStateKey> OutputStates;
        [WriteOnly] public NativeList<int> OutputBudgets;

        public void Execute()
        {
            OutputStates.Clear();
            OutputBudgets.Clear();
            if (InputStateBudgets.Count() == 0)
                return;

            var stateKeys = InputStateBudgets.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < stateKeys.Length; i++)
            {
                var stateKey = stateKeys[i];
                var totalBudget = 0;

                if (!InputStateBudgets.ContainsKey(stateKey))
                    continue;

                InputStateBudgets.TryGetFirstValue(stateKey, out var budget, out var iterator);
                do
                    totalBudget += budget;
                while (InputStateBudgets.TryGetNextValue(out budget, ref iterator));

                InputStateBudgets.Remove(stateKey);

                OutputStates.Add(stateKey);
                OutputBudgets.Add(totalBudget);
            }

            InputStateBudgets.Clear();
            stateKeys.Dispose();
        }
    }

    [BurstCompile]
    struct CollectUnexpandedStates<TStateKey> : IJob
        where TStateKey : unmanaged, IEquatable<TStateKey>
    {
        // Input
        public NativeParallelHashMap<TStateKey, byte> SelectedUnexpandedStates;

        // Output
        public NativeList<TStateKey> SelectedUnexpandedStatesList;

        public void Execute()
        {
            var stateKeys = SelectedUnexpandedStates.GetKeyArray(Allocator.Temp);
            SelectedUnexpandedStatesList.ResizeUninitialized(stateKeys.Length);
            for (int i = 0; i < stateKeys.Length; i++)
                SelectedUnexpandedStatesList[i] = stateKeys[i];
        }
    }
}
