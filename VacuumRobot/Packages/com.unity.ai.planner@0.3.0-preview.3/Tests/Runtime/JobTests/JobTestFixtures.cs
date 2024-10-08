﻿using System;
using KeyDomain;
using Unity.AI.Planner.Jobs;
using Unity.AI.Planner.Tests;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

[assembly: RegisterGenericJobType(typeof(EvaluateNewStatesJob<StateEntityKey, StateData, StateDataContext, TestManualOverrideCumulativeRewardEstimator<StateData>, TestManualOverrideTerminationEvaluator<StateData>>))]

namespace Unity.AI.Planner.Tests
{
    struct TestStateDataContext : IStateDataContext<int, int>
    {
        public int CreateStateData() => default;

        public int GetStateData(int stateKey) => stateKey;

        public int GetStateDataKey(int stateData) => stateData;

        public int CopyStateData(int stateData) => stateData;

        public int RegisterState(int stateData) => stateData;

        public void DestroyState(int stateKey) { }

        public bool Equals(int x, int y) => x == y;

        public int GetHashCode(int stateData) => stateData;
    }

    struct TestStateManager : IStateManager<int, int, TestStateDataContext>
    {
        public TestStateDataContext StateDataContext { get; }
        public TestStateDataContext GetStateDataContext() => new TestStateDataContext();

        public int GetStateData(int stateKey, bool readWrite) => GetStateData(stateKey);

        public IStateData GetStateData(IStateKey stateKey, bool readWrite) => null;

        public int CreateStateData() => default;

        public int CopyStateData(int stateData) => stateData;

        public int CopyState(int stateKey) => stateKey;

        public int GetStateDataKey(int stateData) => stateData;

        public int GetStateData(int stateKey) => stateKey;

        public void DestroyState(int stateKey) { }

        public bool Equals(int x, int y) => x == y;

        public int GetHashCode(int stateData) => stateData;
    }

    struct CountToActionScheduler : IActionScheduler<int, int, TestStateDataContext, TestStateManager, int>
    {
        // Input
        public NativeList<int> UnexpandedStates { get; set; }
        public TestStateManager StateManager { get; set; }

        // Output
        public NativeQueue<StateTransitionInfoPair<int, int, StateTransitionInfo>> CreatedStateInfo { get; set; }

        [BurstCompile]
        struct Add : IJobParallelForDefer
        {
            public TestStateDataContext StateDataContext { get; set; }

            [field:ReadOnly] public NativeArray<int> UnexpandedStates { get; set; }
            [field:NativeDisableContainerSafetyRestriction] public NativeQueue<StateTransitionInfoPair<int, int, StateTransitionInfo>>.ParallelWriter CreatedStateInfo { get; set; }

            public int ValueToAdd;

            public void Execute(int index)
            {
                // Read data from input
                var stateKey = UnexpandedStates[index];
                var stateData = StateDataContext.GetStateData(stateKey);

                // Make modifications to copy of state
                var newStateData = StateDataContext.CopyStateData(stateData);
                newStateData += ValueToAdd;
                var newStateKey = StateDataContext.RegisterState(newStateData);

                var reward = ValueToAdd;

                // Register action. Output transition info (state, action, result, resulting state key).
                CreatedStateInfo.Enqueue(new StateTransitionInfoPair<int, int, StateTransitionInfo>(stateKey, ValueToAdd, newStateKey, new StateTransitionInfo{ Probability = 1f, TransitionUtilityValue = reward }));
            }
        }

        public JobHandle Schedule(JobHandle inputDeps)
        {
            var createdStateInfoConcurrent = CreatedStateInfo.AsParallelWriter();

            var addOneHandle = new Add()
            {
                StateDataContext = StateManager.GetStateDataContext(),
                UnexpandedStates = UnexpandedStates.AsDeferredJobArray(),
                CreatedStateInfo = createdStateInfoConcurrent,
                ValueToAdd = 1,
            }.Schedule(UnexpandedStates, 0, inputDeps);

            var addTwoHandle = new Add()
            {
                StateDataContext = StateManager.GetStateDataContext(),
                UnexpandedStates = UnexpandedStates.AsDeferredJobArray(),
                CreatedStateInfo = createdStateInfoConcurrent,
                ValueToAdd = 2,
            }.Schedule(UnexpandedStates, 0, inputDeps);

            var addThreeHandle = new Add()
            {
                StateDataContext = StateManager.GetStateDataContext(),
                UnexpandedStates = UnexpandedStates.AsDeferredJobArray(),
                CreatedStateInfo = createdStateInfoConcurrent,
                ValueToAdd = 3,
            }.Schedule(UnexpandedStates, 0, inputDeps);

            return JobHandle.CombineDependencies(addOneHandle, addTwoHandle, addThreeHandle);
        }

        public void Run()
        {
            var createdStateInfoConcurrent = CreatedStateInfo.AsParallelWriter();

            var addOneHandle = new Add()
            {
                StateDataContext = StateManager.GetStateDataContext(),
                UnexpandedStates = UnexpandedStates.AsDeferredJobArray(),
                CreatedStateInfo = createdStateInfoConcurrent,
                ValueToAdd = 1,
            }.Schedule(UnexpandedStates, UnexpandedStates.Length);
            addOneHandle.Complete();

            var addTwoHandle = new Add()
            {
                StateDataContext = StateManager.GetStateDataContext(),
                UnexpandedStates = UnexpandedStates.AsDeferredJobArray(),
                CreatedStateInfo = createdStateInfoConcurrent,
                ValueToAdd = 2,
            }.Schedule(UnexpandedStates, UnexpandedStates.Length);
            addTwoHandle.Complete();

            var addThreeHandle = new Add()
            {
                StateDataContext = StateManager.GetStateDataContext(),
                UnexpandedStates = UnexpandedStates.AsDeferredJobArray(),
                CreatedStateInfo = createdStateInfoConcurrent,
                ValueToAdd = 3,
            }.Schedule(UnexpandedStates, UnexpandedStates.Length);
            addThreeHandle.Complete();
        }
    }

    struct CountToDestroyStatesScheduler : IDestroyStatesScheduler<int, int, TestStateDataContext, TestStateManager>
    {
        public TestStateManager StateManager { get; set; }
        public NativeQueue<int> StatesToDestroy { get; set; }

        public JobHandle Schedule(JobHandle inputDeps) => inputDeps;
    }

    struct CountToCumulativeRewardEstimator : ICumulativeRewardEstimator<int>
    {
        public int Goal;

        public BoundedValue Evaluate(int stateData)
            => stateData >= Goal ?
                new float3(0,0,0) : new float3(0, 0, Goal - stateData);
    }

    struct CountToTerminationEvaluator : ITerminationEvaluator<int>
    {
        public int Goal;

        public bool IsTerminal(int stateData, out float terminalReward)
        {
            terminalReward = stateData == Goal ? 0 : -100;
            return stateData >= Goal;
        }
    }

    struct DefaultCumulativeRewardEstimator<TStateData> : ICumulativeRewardEstimator<TStateData>
        where TStateData : unmanaged
    {
        public BoundedValue Evaluate(TStateData stateData) => default;
    }

    struct DefaultTerminalStateEvaluator<TStateData> : ITerminationEvaluator<TStateData>
        where TStateData : unmanaged
    {
        public bool IsTerminal(TStateData stateData, out float terminalReward)
        {
            terminalReward = 0f;
            return false;
        }
    }

    struct TestManualOverrideCumulativeRewardEstimator<TStateData> : ICumulativeRewardEstimator<TStateData>
        where TStateData : unmanaged
    {
#pragma warning disable 649
        public BoundedValue CumulativeRewardEstimatorReturnValue;
#pragma warning restore 649

        public BoundedValue Evaluate(TStateData stateData) => CumulativeRewardEstimatorReturnValue;
    }

    struct TestManualOverrideTerminationEvaluator<TStateData> : ITerminationEvaluator<TStateData>
        where TStateData : unmanaged
    {
#pragma warning disable 649
        public bool TerminationReturnValue;
        public float TerminalRewardValue;
#pragma warning restore 649

        public bool IsTerminal(TStateData stateData, out float terminalReward)
        {
            terminalReward = TerminalRewardValue;
            return TerminationReturnValue;
        }
    }
}
