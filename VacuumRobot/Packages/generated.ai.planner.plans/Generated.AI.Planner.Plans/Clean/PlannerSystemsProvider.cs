using System;
using Unity.AI.Planner;
using Unity.AI.Planner.Traits;
using Unity.AI.Planner.Jobs;
using Unity.Entities;
using Unity.Jobs;
using Generated.AI.Planner.StateRepresentation.Clean;

[assembly: RegisterGenericJobType(typeof(EvaluateNewStatesJob<StateEntityKey, StateData, StateDataContext,
    global::AI.Planner.Actions.Clean.CustomVacuumRobotHeuristic, Generated.AI.Planner.Plans.Clean.TerminationEvaluator>))]
[assembly: RegisterGenericJobType(typeof(PlannerScheduler<StateEntityKey, ActionKey, StateManager, StateData, StateDataContext,
    Generated.AI.Planner.Plans.Clean.ActionScheduler, global::AI.Planner.Actions.Clean.CustomVacuumRobotHeuristic, Generated.AI.Planner.Plans.Clean.TerminationEvaluator, DestroyStatesJobScheduler>.CopyPlanDataJob))]

namespace Generated.AI.Planner.Plans.Clean
{
    public class PlanningSystemsProvider : IPlanningSystemsProvider
    {
        public ITraitBasedStateConverter StateConverter => m_StateConverter;
        PlannerStateConverter<TraitBasedObject, StateEntityKey, StateData, StateDataContext, StateManager> m_StateConverter;

        public ITraitBasedPlanExecutor PlanExecutor => m_Executor;
        CleanExecutor m_Executor;

        public IPlannerScheduler PlannerScheduler => m_Scheduler;
        PlannerScheduler<StateEntityKey, ActionKey, StateManager, StateData, StateDataContext, ActionScheduler, global::AI.Planner.Actions.Clean.CustomVacuumRobotHeuristic, TerminationEvaluator, DestroyStatesJobScheduler> m_Scheduler;

        public void Initialize(ProblemDefinition problemDefinition, string planningSimulationWorldName)
        {
            var world = new World(planningSimulationWorldName);
            var stateManager = world.GetOrCreateSystem<StateManager>();

            m_StateConverter = new PlannerStateConverter<TraitBasedObject, StateEntityKey, StateData, StateDataContext, StateManager>(problemDefinition, stateManager);

            m_Scheduler = new PlannerScheduler<StateEntityKey, ActionKey, StateManager, StateData, StateDataContext, ActionScheduler, global::AI.Planner.Actions.Clean.CustomVacuumRobotHeuristic, TerminationEvaluator, DestroyStatesJobScheduler>();
            m_Scheduler.Initialize(stateManager, new global::AI.Planner.Actions.Clean.CustomVacuumRobotHeuristic(), new TerminationEvaluator(), problemDefinition.DiscountFactor);

            m_Executor = new CleanExecutor(stateManager, m_StateConverter);

            // Ensure planning jobs are not running when destroying the state manager
            stateManager.Destroying += () => m_Scheduler.CurrentJobHandle.Complete();
        }
    }
}
