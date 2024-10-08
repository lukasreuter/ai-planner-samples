using System;
using System.Collections;
using Unity.AI.Planner.Controller;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.AI.Planner.Traits
{
    abstract class BaseTraitBasedPlanExecutor<TObject, TStateKey, TStateData, TStateDataContext, TStateManager, TActionKey> : ITraitBasedPlanExecutor
        where TObject : unmanaged, ITraitBasedObject
        where TStateKey : unmanaged, IEquatable<TStateKey>, IStateKey
        where TActionKey : unmanaged, IEquatable<TActionKey>, IActionKey
        where TStateData : unmanaged, ITraitBasedStateData<TObject, TStateData>
        where TStateDataContext : struct, ITraitBasedStateDataContext<TObject, TStateKey, TStateData>
        where TStateManager : SystemBase, ITraitBasedStateManager<TObject, TStateKey, TStateData, TStateDataContext>
    {
        struct DecisionRuntimeInfo
        {
            public float StartTimestamp;
            public void Reset() => StartTimestamp = Time.time;
        }

        /// <summary>
        /// Status of the plan executor.
        /// </summary>
        public PlanExecutionStatus Status { get; private set; } = PlanExecutionStatus.AwaitingPlan;

        /// <summary>
        /// The plan the executor is following.
        /// </summary>
        IPlan IPlanExecutor.Plan => m_PlanWrapper;
        protected PlanWrapper<TStateKey, TActionKey, TStateManager, TStateData, TStateDataContext> m_PlanWrapper;

        /// <summary>
        /// State key for the current state of the plan executor.
        /// </summary>
        IStateKey IPlanExecutor.CurrentExecutorStateKey => CurrentExecutorState;
        protected TStateKey CurrentExecutorState { get; private set; }

        IStateKey IPlanExecutor.CurrentPlanStateKey => CurrentPlanState;
        protected TStateKey CurrentPlanState { get; private set;}

        /// <summary>
        /// State data for the current state of the plan executor.
        /// </summary>
        IStateData IPlanExecutor.CurrentStateData => CurrentStateData;
        protected TStateData CurrentStateData => m_StateManager.GetStateData(CurrentExecutorState, false);

        /// <summary>
        /// Action key for the current action being executed.
        /// </summary>
        IActionKey IPlanExecutor.CurrentActionKey => CurrentActionKey;
        protected TActionKey CurrentActionKey { get; set; }

        protected MonoBehaviour m_Actor;
        protected TStateManager m_StateManager;
        protected ObjectCorrespondence m_PlanStateToGameStateIdLookup = new ObjectCorrespondence(1, Allocator.Persistent);

        PlanExecutionSettings m_ExecutionSettings;

        DecisionRuntimeInfo m_DecisionRuntimeInfo;
        Coroutine m_CurrentActionCoroutine;
        ActionExecutionInfo[] m_ActionExecuteInfos;

        Action<IActionKey> m_OnActionComplete;
        Action<IStateKey> m_OnTerminalStateReached;
        Action<IStateKey> m_OnUnexpectedState;

        protected abstract void Act(TActionKey act);
        public abstract string GetActionName(IActionKey actionKey); //todo move to planWrapper
        public abstract ActionParameterInfo[] GetActionParametersInfo(IStateKey stateKey, IActionKey actionKey);

        public void SetExecutionSettings(MonoBehaviour actor, ActionExecutionInfo[] actionExecutionInfos, PlanExecutionSettings executionSettings, Action<IActionKey> onActionComplete = null, Action<IStateKey> onTerminalStateReached = null, Action<IStateKey> onUnexpectedState = null)
        {
            m_Actor = actor;
            m_ActionExecuteInfos = actionExecutionInfos;
            m_ExecutionSettings = executionSettings;
            m_OnActionComplete = onActionComplete;
            m_OnTerminalStateReached = onTerminalStateReached;
            m_OnUnexpectedState = onUnexpectedState;
        }

        public void SetPlan(IPlan plan)
        {
            if (!(plan is PlanWrapper<TStateKey, TActionKey, TStateManager, TStateData, TStateDataContext> planWrapper))
                throw new ArgumentException($"Plan must be of type {typeof(PlanWrapper<TStateKey, TActionKey, TStateManager, TStateData, TStateDataContext>)}.");

            if (Status == PlanExecutionStatus.AwaitingPlan)
                Status = PlanExecutionStatus.AwaitingExecution;

            m_PlanWrapper = planWrapper;
        }

        public void UpdateCurrentState(IStateKey stateKey)
        {
            if (!(stateKey is TStateKey newExecutorState))
                throw new ArgumentException($"Expected state key of type {typeof(TStateKey)}. Received state key of type {stateKey?.GetType()}.");

            // Don't destroy the current state if the same key/data are used
            if (!newExecutorState.Equals(CurrentExecutorState))
                m_StateManager.DestroyState(CurrentExecutorState);

            // If the user has passed in a plan state, use a copy instead (so we don't mutate plan states).
            // Fixme: when executor and plan states use separate worlds.
            var matchingPlanState = default(TStateKey);
            if (m_PlanWrapper != null && FindMatchingStateInPlan(newExecutorState, out matchingPlanState) && stateKey.Equals(matchingPlanState))
                newExecutorState = m_StateManager.CopyState(newExecutorState); // Don't use plan states as executor states.

            // Assign new state
            CurrentExecutorState = newExecutorState;
            CurrentPlanState = matchingPlanState;

            // Check for terminal or unexpected state
            if (m_PlanWrapper != null)
                CheckNewState();
        }

        public void UpdateCurrentState(IStateData stateData)
        {
            if (!(stateData is TStateData castStateData))
                throw new ArgumentException($"Expected state data of type {typeof(TStateData)}. Received state data of type {stateData?.GetType()}.");

            var updatedKey = m_StateManager.GetStateDataKey(castStateData);
            UpdateCurrentState(updatedKey);
        }

        public bool ReadyToAct()
        {
            // Check if there isn't an assigned plan or if currently executing an action
            if (m_PlanWrapper == null || !CurrentActionKey.Equals(default))
                return false;

            // Check for a corresponding plan state, if not already assigned
            if (CurrentPlanState.Equals(default) && m_PlanWrapper.TryGetEquivalentPlanState(CurrentExecutorState, out var planStateKey))
                CurrentPlanState = planStateKey;

            // Check for immediate decision info
            if (!m_PlanWrapper.TryGetStateInfo(CurrentPlanState, out var stateInfo)
                || !m_PlanWrapper.TryGetOptimalAction(CurrentPlanState, out _))
                return false;

            // Check user-specified condition
            switch (m_ExecutionSettings.ExecutionMode)
            {
                case PlanExecutionSettings.PlanExecutionMode.ActImmediately:
                    return true;

                case PlanExecutionSettings.PlanExecutionMode.WaitForManualExecutionCall:
                    return false;

                case PlanExecutionSettings.PlanExecutionMode.WaitForPlanCompletion:
                    return stateInfo.SubplanIsComplete;

                case PlanExecutionSettings.PlanExecutionMode.WaitForMaximumDecisionTolerance:
                    return stateInfo.CumulativeRewardEstimate.Range <= m_ExecutionSettings.MaximumDecisionTolerance;

                case PlanExecutionSettings.PlanExecutionMode.WaitForMinimumPlanSize:
                    return m_PlanWrapper.Size >= m_ExecutionSettings.MinimumPlanSize || stateInfo.SubplanIsComplete;

                case PlanExecutionSettings.PlanExecutionMode.WaitForMinimumPlanningTime:
                    return Time.time - m_DecisionRuntimeInfo.StartTimestamp >= m_ExecutionSettings.MinimumPlanningTime || stateInfo.SubplanIsComplete;

                default:
                    return true;
            }
        }

        public void ExecuteNextAction(IActionKey overrideAction = null)
        {
            if (m_PlanWrapper == null)
            {
                Debug.LogError("No plan assigned on the plan executor.");
                return;
            }

            // Reset decision time tracker
            m_DecisionRuntimeInfo.Reset();

            // Check for a corresponding plan state, if not already assigned
            if (CurrentPlanState.Equals(default) && m_PlanWrapper.TryGetEquivalentPlanState(CurrentExecutorState, out var planStateKey))
                CurrentPlanState = planStateKey;

            // Use specified action
            if (overrideAction != null)
            {
                if (!(overrideAction is TActionKey typedAction))
                    throw new ArgumentException($"Expected override action key of type {typeof(TActionKey)}. Received key of type {overrideAction.GetType()}.");

                if (!m_PlanWrapper.TryGetActionInfo(CurrentPlanState, typedAction, out _))
                    throw new ArgumentException($"Action {typedAction} for state {CurrentPlanState} was not found in the plan.");

                Status = PlanExecutionStatus.ExecutingAction;
                CurrentActionKey = typedAction;
                Act(typedAction);
                return;
            }

            // No manual override; use current best action
            if (!m_PlanWrapper.TryGetOptimalAction(CurrentPlanState, out var actionKey))
            {
                Debug.LogError($"No actions available for plan state {CurrentPlanState}.");
                Status = PlanExecutionStatus.AwaitingExecution;
                CurrentActionKey = default;
            }
            else
            {
                Status = PlanExecutionStatus.ExecutingAction;
                CurrentActionKey = actionKey;
                Act(CurrentActionKey);
            }
        }

        public void StopExecution()
        {
            Status = PlanExecutionStatus.AwaitingExecution;
            CurrentActionKey = default;

            if (m_CurrentActionCoroutine != null)
            {
                m_Actor.StopCoroutine(m_CurrentActionCoroutine);
                m_CurrentActionCoroutine = null;
            }
        }

        protected ActionExecutionInfo GetExecutionInfo(string actionName)
        {
            for (int i = 0; i < m_ActionExecuteInfos.Length; i++)
            {
                var info = m_ActionExecuteInfos[i];
                if (info.IsValidForAction(actionName))
                    return info;
            }

            return null;
        }

        protected void StartAction(ActionExecutionInfo executionInfo, object[] arguments)
        {
            Assert.IsNull(m_CurrentActionCoroutine);

            if (executionInfo.InvokeMethod(arguments) is IEnumerator actionCoroutine)
            {
                Assert.IsNotNull(m_Actor, "No actor assigned on the plan executor. Cannot start coroutine.");

                // Begin action coroutine
                m_CurrentActionCoroutine = m_Actor.StartCoroutine(actionCoroutine);
                m_Actor.StartCoroutine(WaitForAction());
            }
            else
            {
                // Immediately complete action
                CompleteAction();
            }
        }

        void CheckNewState()
        {
            if (CurrentPlanState.Equals(default))
            {
                // Don't change the plan here. Let users decide what to do.
                Status = PlanExecutionStatus.AwaitingExecution;
                m_OnUnexpectedState?.Invoke(CurrentExecutorState);
            }
            else if (IsTerminal(CurrentPlanState))
            {
                // Reached terminal state -> no more plan to execute.
                Status = PlanExecutionStatus.AwaitingPlan;
                m_OnTerminalStateReached?.Invoke(CurrentPlanState);
            }
            else
            {
                Status = PlanExecutionStatus.AwaitingExecution;
            }
        }

        bool FindMatchingStateInPlan(TStateKey stateKey, out TStateKey planStateKey)
        {
            planStateKey = default;
            m_PlanStateToGameStateIdLookup.Clear();

            if (!m_PlanWrapper.TryGetEquivalentPlanState(stateKey, out var matchingKey))
                return false;

            planStateKey = matchingKey;
            var planStateData = m_StateManager.GetStateData(planStateKey, false);
            var inputStateData = m_StateManager.GetStateData(stateKey, false);

            // Map the plan state to the input state
            return planStateData.TryGetObjectMapping(inputStateData, m_PlanStateToGameStateIdLookup);
        }

        bool IsTerminal(TStateKey stateKey)
        {
            return m_PlanWrapper.TryGetStateInfo(stateKey, out var stateInfo)
                    && stateInfo.SubplanIsComplete
                    && !m_PlanWrapper.TryGetOptimalAction(stateKey, out _);
        }

        IEnumerator WaitForAction()
        {
            yield return m_CurrentActionCoroutine;

            CompleteAction();
        }

        void CompleteAction()
        {
            m_OnActionComplete?.Invoke(CurrentActionKey);
            m_CurrentActionCoroutine = null;
            CurrentActionKey = default;
        }

        public void Dispose()
        {
            m_PlanStateToGameStateIdLookup.Dispose();
        }
    }
}
