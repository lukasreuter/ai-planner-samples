﻿using System;
using NUnit.Framework;
using Unity.AI.Planner.Jobs;
using Unity.AI.Planner.Tests.Unit;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unity.AI.Planner.Tests.Unit
{
    [Category("Unit")]
    class NewStateEvaluationJobTests
    {
        internal struct StateValueAsCumulativeRewardEstimatorValue : ICumulativeRewardEstimator<int>
        {
            public BoundedValue Evaluate(int stateData) => new float3(stateData, stateData, stateData);
        }

        struct EvensTerminalStateEvaluator : ITerminationEvaluator<int>
        {
            public bool IsTerminal(int stateData, out float terminalReward)
            {
                terminalReward = stateData;
                return stateData % 2 == 0;
            }
        }

        struct ExceptionCumulativeRewardEstimator : ICumulativeRewardEstimator<int>
        {
            public BoundedValue Evaluate(int stateData)
            {
                throw new Exception("Should not be thrown.");
            }
        }

        struct ExceptionTerminationEvaluator : ITerminationEvaluator<int>
        {
            public bool IsTerminal(int stateData, out float terminalReward)
            {
                terminalReward = 0f;
                throw new Exception("Should not be thrown.");
            }
        }

        [Test]
        public void DoesNotExecuteWithoutStates()
        {
            var states = new NativeList<int>(0, Allocator.TempJob);
            var stateInfoLookup = new NativeParallelHashMap<int, StateInfo>(0, Allocator.TempJob);
            var binnedStateKeys = new NativeParallelMultiHashMap<int, int>(1, Allocator.TempJob);

            var stateEvaluationJob = new EvaluateNewStatesJob<int, int, TestStateDataContext, ExceptionCumulativeRewardEstimator, ExceptionTerminationEvaluator>()
            {
                StateDataContext = new TestStateDataContext(),
                StateInfoLookup = stateInfoLookup.AsParallelWriter(),
                States = states.AsDeferredJobArray(),
                BinnedStateKeys = binnedStateKeys.AsParallelWriter(),
            };

            Assert.DoesNotThrow(() => stateEvaluationJob.Schedule(states, default).Complete());

            states.Dispose();
            stateInfoLookup.Dispose();
            binnedStateKeys.Dispose();
        }

        [Test]
        public void EvaluateCumulativeRewardEstimatorMultipleStates()
        {
            const int kStateCount = 10;
            var states = new NativeList<int>(kStateCount, Allocator.TempJob);
            var stateInfoLookup = new NativeParallelHashMap<int, StateInfo>(kStateCount, Allocator.TempJob);
            var binnedStateKeys = new NativeParallelMultiHashMap<int, int>(kStateCount, Allocator.TempJob);

            for (int i = 0; i < kStateCount; i++)
            {
                states.Add(i);
            }

            var stateEvaluationJob = new EvaluateNewStatesJob<int, int, TestStateDataContext, StateValueAsCumulativeRewardEstimatorValue, DefaultTerminalStateEvaluator<int>>
            {
                StateDataContext = new TestStateDataContext(),
                StateInfoLookup = stateInfoLookup.AsParallelWriter(),
                States = states.AsDeferredJobArray(),
                BinnedStateKeys = binnedStateKeys.AsParallelWriter(),
            };
            stateEvaluationJob.Schedule(states, default).Complete();

            for (int i = 0; i < states.Length; i++)
            {
                stateInfoLookup.TryGetValue(i, out var stateInfo);

                Assert.AreEqual(new BoundedValue(i, i, i), stateInfo.CumulativeRewardEstimate);
            }

            states.Dispose();
            stateInfoLookup.Dispose();
            binnedStateKeys.Dispose();
        }

        [Test]
        public void EvaluateTerminationMultipleStates()
        {
            const int kStateCount = 10;
            var states = new NativeList<int>(kStateCount, Allocator.TempJob);
            var stateInfoLookup = new NativeParallelHashMap<int, StateInfo>(kStateCount, Allocator.TempJob);
            var binnedStateKeys = new NativeParallelMultiHashMap<int, int>(kStateCount, Allocator.TempJob);

            for (int i = 0; i < kStateCount; i++)
            {
                states.Add(i);
            }

            var stateEvaluationJob = new EvaluateNewStatesJob<int, int, TestStateDataContext, DefaultCumulativeRewardEstimator<int>, EvensTerminalStateEvaluator>
            {
                StateDataContext = new TestStateDataContext(),
                StateInfoLookup = stateInfoLookup.AsParallelWriter(),
                States = states.AsDeferredJobArray(),
                BinnedStateKeys = binnedStateKeys.AsParallelWriter(),
            };
            stateEvaluationJob.Schedule(states, default).Complete();

            for (int i = 0; i < states.Length; i++)
            {
                stateInfoLookup.TryGetValue(i, out var stateInfo);

                Assert.AreEqual(i % 2 == 0, stateInfo.SubplanIsComplete);
            }

            states.Dispose();
            stateInfoLookup.Dispose();
            binnedStateKeys.Dispose();
        }
    }
}

#if ENABLE_PERFORMANCE_TESTS
namespace Unity.AI.Planner.Tests.Performance
{
    using PerformanceTesting;

    [Category("Performance")]
    public class NewStateEvaluationJobPerformanceTests
    {
        [Performance, Test]
        public void TestEvaluateMultipleStates()
        {
            const int kStateCount = 1000;

            NativeList<int> states = default;
            NativeParallelHashMap<int, StateInfo> stateInfoLookup = default;
            NativeParallelMultiHashMap<int, int> binnedStateKeys = default;

            Measure.Method(() =>
            {
                var stateEvaluationJob = new EvaluateNewStatesJob<int, int, TestStateDataContext,
                    NewStateEvaluationJobTests.StateValueAsCumulativeRewardEstimatorValue, DefaultTerminalStateEvaluator<int>>
                {
                    StateDataContext = new TestStateDataContext(),
                    States = states.AsDeferredJobArray(),

                    StateInfoLookup = stateInfoLookup.AsParallelWriter(),
                    BinnedStateKeys = binnedStateKeys.AsParallelWriter(),
                };
                stateEvaluationJob.Schedule(states, default).Complete();
            }).SetUp(() =>
            {
                states = new NativeList<int>(kStateCount, Allocator.TempJob);
                stateInfoLookup = new NativeParallelHashMap<int, StateInfo>(kStateCount, Allocator.TempJob);
                binnedStateKeys = new NativeParallelMultiHashMap<int, int>(kStateCount, Allocator.TempJob);

                for (int i = 0; i < kStateCount; i++)
                {
                    states.Add(i);
                }
            }).CleanUp(() =>
            {
                states.Dispose();
                stateInfoLookup.Dispose();
                binnedStateKeys.Dispose();
            }).WarmupCount(1).MeasurementCount(30).IterationsPerMeasurement(1).Run();
        }
    }
}
#endif
