﻿using System;
using KeyDomain;
using NUnit.Framework;
using Unity.AI.Planner;
using Unity.AI.Planner.Jobs;
using Unity.AI.Planner.Tests;
using Unity.Collections;
using Unity.Jobs;
using DefaultScheduler = Unity.AI.Planner.PlannerScheduler<int, int, Unity.AI.Planner.Tests.TestStateManager, int, Unity.AI.Planner.Tests.TestStateDataContext, Unity.AI.Planner.Tests.CountToActionScheduler, Unity.AI.Planner.Tests.DefaultCumulativeRewardEstimator<int>, Unity.AI.Planner.Tests.DefaultTerminalStateEvaluator<int>, Unity.AI.Planner.Tests.Integration.DestroyIntsScheduler>;
using CountToScheduler = Unity.AI.Planner.PlannerScheduler<int, int, Unity.AI.Planner.Tests.TestStateManager, int, Unity.AI.Planner.Tests.TestStateDataContext, Unity.AI.Planner.Tests.CountToActionScheduler, Unity.AI.Planner.Tests.CountToCumulativeRewardEstimator, Unity.AI.Planner.Tests.CountToTerminationEvaluator, Unity.AI.Planner.Tests.Integration.DestroyIntsScheduler>;
using CountStateScheduler = Unity.AI.Planner.PlannerScheduler<int, int, Unity.AI.Planner.Tests.TestStateManager, int, Unity.AI.Planner.Tests.TestStateDataContext, Unity.AI.Planner.Tests.CountToActionScheduler, Unity.AI.Planner.Tests.CountToCumulativeRewardEstimator, Unity.AI.Planner.Tests.CountToTerminationEvaluator, Unity.AI.Planner.Tests.CountToDestroyStatesScheduler>;

[assembly: RegisterGenericJobType(typeof(Unity.AI.Planner.Jobs.GraphExpansionJob<int, int, TestStateDataContext, int>))]
[assembly: RegisterGenericJobType(typeof(Unity.AI.Planner.Jobs.BackpropagationJob<int, int>))]
[assembly: RegisterGenericJobType(typeof(Unity.AI.Planner.Jobs.QueueToListJob<int>))]
[assembly: RegisterGenericJobType(typeof(Unity.AI.Planner.Jobs.SelectionJob<int, int>))]
[assembly: RegisterGenericJobType(typeof(Unity.AI.Planner.Jobs.PrepareForExpansionJob<int, int>))]

[assembly: RegisterGenericJobType(typeof(EvaluateNewStatesJob<int, int, TestStateDataContext, DefaultCumulativeRewardEstimator<int>, DefaultTerminalStateEvaluator<int>>))]
[assembly: RegisterGenericJobType(typeof(EvaluateNewStatesJob<int, int, TestStateDataContext, CountToCumulativeRewardEstimator, CountToTerminationEvaluator>))]
[assembly: RegisterGenericJobType(typeof(DefaultScheduler.CopyPlanDataJob))]
[assembly: RegisterGenericJobType(typeof(CountToScheduler.CopyPlanDataJob))]
[assembly: RegisterGenericJobType(typeof(CountStateScheduler.CopyPlanDataJob))]

namespace Unity.AI.Planner.Tests.Integration
{

    struct DestroyIntsScheduler : IDestroyStatesScheduler<int, int, TestStateDataContext, TestStateManager>
    {
        public TestStateManager StateManager { get; set; }
        public NativeQueue<int> StatesToDestroy { get; set; }
        public JobHandle Schedule(JobHandle inputDeps)
        {
            return inputDeps;
        }
    }

    [Category("Integration")]
    [TestFixture]
    class PlannerSchedulerTests
    {
        [Test]
        public void TestTenIterations()
        {
            var scheduler = new DefaultScheduler();
            scheduler.Initialize(new TestStateManager(), new DefaultCumulativeRewardEstimator<int>(), new DefaultTerminalStateEvaluator<int>());
            JobHandle currentJobHandle = default;

            for (int i = 0; i < 10; i++)
            {
                currentJobHandle = scheduler.Schedule(default);
                currentJobHandle.Complete();
            }

            currentJobHandle.Complete();
            scheduler.Dispose();
        }

        [Test]
        public void TestUntilCompletion()
        {
            const int k_RootState = 0;
            const int k_Goal = 100;
            var scheduler = new CountToScheduler();
            scheduler.Initialize(new TestStateManager(), new CountToCumulativeRewardEstimator { Goal = k_Goal }, new CountToTerminationEvaluator { Goal = k_Goal });

            bool complete = false;
            var request = scheduler.RequestPlan(k_RootState).PlanUntil(requestCompleteCallback: (plan) => {complete = true;});
            while (!complete)
            {
                scheduler.Schedule(default).Complete();
            }

            var planSize = request.Plan.Size;
            request.Dispose();
            scheduler.Dispose();

            Assert.IsTrue(complete);
            Assert.AreEqual(103, planSize);
        }

        [Test]
        public void TestGraphPruning()
        {
            const int k_RootState = 0;
            const int k_Goal = 100;
            var scheduler = new CountToScheduler();
            scheduler.Initialize(new TestStateManager(), new CountToCumulativeRewardEstimator { Goal = k_Goal }, new CountToTerminationEvaluator { Goal = k_Goal });
            var request = scheduler.RequestPlan(k_RootState);
            for (int i = 0; i < 10; i++)
            {
                scheduler.Schedule(default).Complete();
            }

            scheduler.UpdatePlanRequestRootState(1);
            scheduler.CurrentJobHandle.Complete();

            var planRootState = scheduler.m_PlanData.RootStateKey;
            var zeroInPlan = scheduler.m_PlanData.StateDepthLookup.ContainsKey(k_RootState);
            request.Dispose();
            scheduler.Dispose();

            Assert.AreEqual(1, planRootState);
            Assert.IsFalse(zeroInPlan);
        }

        [Test]
        public void UpdateRootState_AfterSchedule_DoesNotThrow()
        {
            const int k_RootState = 0;

            var scheduler = new DefaultScheduler();
            scheduler.Initialize(new TestStateManager(), new DefaultCumulativeRewardEstimator<int>(), new DefaultTerminalStateEvaluator<int>());
            scheduler.RequestPlan(k_RootState);
            var jobHandle = scheduler.Schedule(default);

            Assert.DoesNotThrow(() => scheduler.UpdatePlanRequestRootState(k_RootState+1));

            jobHandle.Complete();
            scheduler.Dispose();
        }

        [Test]
        public void RequestPlan_AfterSchedule_DoesNotThrow()
        {
            const int k_RootState = 0;

            var scheduler = new DefaultScheduler();
            scheduler.Initialize(new TestStateManager(), new DefaultCumulativeRewardEstimator<int>(), new DefaultTerminalStateEvaluator<int>());
            scheduler.RequestPlan(k_RootState);
            var jobHandle = scheduler.Schedule(default);

            Assert.DoesNotThrow(() => scheduler.RequestPlan(k_RootState+1));

            jobHandle.Complete();
            scheduler.Dispose();
        }

        [Test]
        public void SetTermination_AfterSchedule_DoesNotThrow()
        {
            const int k_RootState = 0;

            var scheduler = new DefaultScheduler();
            scheduler.Initialize(new TestStateManager(), new DefaultCumulativeRewardEstimator<int>(), new DefaultTerminalStateEvaluator<int>());
            scheduler.RequestPlan(k_RootState);
            var jobHandle = scheduler.Schedule(default);

            Assert.DoesNotThrow(() => scheduler.SetTerminationEvaluator(new DefaultTerminalStateEvaluator<int>()));

            jobHandle.Complete();
            scheduler.Dispose();
        }

        [Test]
        public void Schedule_AfterSchedule_DoesNotThrow()
        {
            const int k_RootState = 0;

            var scheduler = new DefaultScheduler();
            scheduler.Initialize(new TestStateManager(), new DefaultCumulativeRewardEstimator<int>(), new DefaultTerminalStateEvaluator<int>());
            scheduler.RequestPlan(k_RootState);
            var jobHandle = scheduler.Schedule(default);

            Assert.DoesNotThrow(() => jobHandle = scheduler.Schedule(jobHandle));

            jobHandle.Complete();
            scheduler.Dispose();
        }
    }
}

#if ENABLE_PERFORMANCE_TESTS
namespace Unity.AI.Planner.Tests.Performance
{
    using PerformanceTesting;

    [Category("Performance")]
    class PlannerSchedulerPerformanceTests
    {
        [Performance, Test]
        public void ProfileCountToGoalScheduled()
        {
            const int kRootState = 0;
            const int kGoal = 42;
            PlannerScheduler<int, int, TestStateManager, int, TestStateDataContext, CountToActionScheduler, CountToCumulativeRewardEstimator, CountToTerminationEvaluator, CountToDestroyStatesScheduler> scheduler = default;

            Measure.Method(() =>
            {
                var planRequest = scheduler.RequestPlan(kRootState);
                while (planRequest.Status != PlanRequestStatus.Complete)
                {
                    scheduler.Schedule(default).Complete();
                }
            }).SetUp(() =>
            {
                scheduler = new PlannerScheduler<int, int, TestStateManager, int, TestStateDataContext, CountToActionScheduler, CountToCumulativeRewardEstimator, CountToTerminationEvaluator, CountToDestroyStatesScheduler>();
                scheduler.Initialize(new TestStateManager(), new CountToCumulativeRewardEstimator { Goal = kGoal }, new CountToTerminationEvaluator { Goal = kGoal }, 1.0f);
            }).CleanUp(() =>
            {
                scheduler.Dispose();
            }).MeasurementCount(30).IterationsPerMeasurement(1).Run();

            PerformanceUtility.AssertRange(0.8f, 13);
        }
    }
}
#endif
