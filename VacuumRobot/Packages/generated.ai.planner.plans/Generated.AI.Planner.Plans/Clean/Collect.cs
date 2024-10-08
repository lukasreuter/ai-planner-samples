using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.AI.Planner;
using Unity.AI.Planner.Traits;
using Unity.Burst;
using Generated.AI.Planner.StateRepresentation;
using Generated.AI.Planner.StateRepresentation.Clean;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Generated.AI.Planner.Plans.Clean
{
    [BurstCompile]
    struct Collect : IJobParallelForDefer
    {
        public Guid ActionGuid;
        
        const int k_RobotIndex = 0;
        const int k_DirtIndex = 1;
        const int k_MaxArguments = 2;

        public static readonly string[] parameterNames = {
            "Robot",
            "Dirt",
        };

        [ReadOnly] NativeArray<StateEntityKey> m_StatesToExpand;
        StateDataContext m_StateDataContext;

        // local allocations
        [NativeDisableContainerSafetyRestriction] NativeArray<ComponentType> RobotFilter;
        [NativeDisableContainerSafetyRestriction] NativeList<int> RobotObjectIndices;
        [NativeDisableContainerSafetyRestriction] NativeArray<ComponentType> DirtFilter;
        [NativeDisableContainerSafetyRestriction] NativeList<int> DirtObjectIndices;

        [NativeDisableContainerSafetyRestriction] NativeList<ActionKey> ArgumentPermutations;
        [NativeDisableContainerSafetyRestriction] NativeList<CollectFixupReference> TransitionInfo;

        bool LocalContainersInitialized => ArgumentPermutations.IsCreated;

        internal Collect(Guid guid, NativeList<StateEntityKey> statesToExpand, StateDataContext stateDataContext)
        {
            ActionGuid = guid;
            m_StatesToExpand = statesToExpand.AsDeferredJobArray();
            m_StateDataContext = stateDataContext;
            RobotFilter = default;
            RobotObjectIndices = default;
            DirtFilter = default;
            DirtObjectIndices = default;
            ArgumentPermutations = default;
            TransitionInfo = default;
        }

        void InitializeLocalContainers()
        {
            RobotFilter = new NativeArray<ComponentType>(2, Allocator.Temp){[0] = ComponentType.ReadWrite<Robot>(),[1] = ComponentType.ReadWrite<Location>(),  };
            RobotObjectIndices = new NativeList<int>(2, Allocator.Temp);
            DirtFilter = new NativeArray<ComponentType>(2, Allocator.Temp){[0] = ComponentType.ReadWrite<Dirt>(),[1] = ComponentType.ReadWrite<Location>(),  };
            DirtObjectIndices = new NativeList<int>(2, Allocator.Temp);

            ArgumentPermutations = new NativeList<ActionKey>(4, Allocator.Temp);
            TransitionInfo = new NativeList<CollectFixupReference>(ArgumentPermutations.Length, Allocator.Temp);
        }

        public static int GetIndexForParameterName(string parameterName)
        {
            
            if (string.Equals(parameterName, "Robot", StringComparison.OrdinalIgnoreCase))
                 return k_RobotIndex;
            if (string.Equals(parameterName, "Dirt", StringComparison.OrdinalIgnoreCase))
                 return k_DirtIndex;

            return -1;
        }

        void GenerateArgumentPermutations(StateData stateData, NativeList<ActionKey> argumentPermutations)
        {
            RobotObjectIndices.Clear();
            stateData.GetTraitBasedObjectIndices(RobotObjectIndices, RobotFilter);
            
            DirtObjectIndices.Clear();
            stateData.GetTraitBasedObjectIndices(DirtObjectIndices, DirtFilter);
            
            var LocationBuffer = stateData.LocationBuffer;
            
            

            for (int i0 = 0; i0 < RobotObjectIndices.Length; i0++)
            {
                var RobotIndex = RobotObjectIndices[i0];
                var RobotObject = stateData.TraitBasedObjects[RobotIndex];
                
                
                
            
            

            for (int i1 = 0; i1 < DirtObjectIndices.Length; i1++)
            {
                var DirtIndex = DirtObjectIndices[i1];
                var DirtObject = stateData.TraitBasedObjects[DirtIndex];
                
                if (!(LocationBuffer[RobotObject.LocationIndex].Position == LocationBuffer[DirtObject.LocationIndex].Position))
                    continue;
                
                

                var actionKey = new ActionKey(k_MaxArguments) {
                                                        ActionGuid = ActionGuid,
                                                       [k_RobotIndex] = RobotIndex,
                                                       [k_DirtIndex] = DirtIndex,
                                                    };
                argumentPermutations.Add(actionKey);
            
            }
            
            }
        }

        StateTransitionInfoPair<StateEntityKey, ActionKey, StateTransitionInfo> ApplyEffects(ActionKey action, StateEntityKey originalStateEntityKey)
        {
            var originalState = m_StateDataContext.GetStateData(originalStateEntityKey);
            var originalStateObjectBuffer = originalState.TraitBasedObjects;

            var newState = m_StateDataContext.CopyStateData(originalState);

            
            newState.RemoveTraitBasedObjectAtIndex(action[k_DirtIndex]);

            var reward = Reward(originalState, action, newState);
            var StateTransitionInfo = new StateTransitionInfo { Probability = 1f, TransitionUtilityValue = reward };
            var resultingStateKey = m_StateDataContext.GetStateDataKey(newState);

            return new StateTransitionInfoPair<StateEntityKey, ActionKey, StateTransitionInfo>(originalStateEntityKey, action, resultingStateKey, StateTransitionInfo);
        }

        float Reward(StateData originalState, ActionKey action, StateData newState)
        {
            var reward = 10f;

            return reward;
        }

        public void Execute(int jobIndex)
        {
            if (!LocalContainersInitialized)
                InitializeLocalContainers();

            m_StateDataContext.JobIndex = jobIndex;

            var stateEntityKey = m_StatesToExpand[jobIndex];
            var stateData = m_StateDataContext.GetStateData(stateEntityKey);

            ArgumentPermutations.Clear();
            GenerateArgumentPermutations(stateData, ArgumentPermutations);

            TransitionInfo.Clear();
            TransitionInfo.Capacity = math.max(TransitionInfo.Capacity, ArgumentPermutations.Length);
            for (var i = 0; i < ArgumentPermutations.Length; i++)
            {
                TransitionInfo.Add(new CollectFixupReference { TransitionInfo = ApplyEffects(ArgumentPermutations[i], stateEntityKey) });
            }

            // fixups
            var stateEntity = stateEntityKey.Entity;
            var fixupBuffer = m_StateDataContext.EntityCommandBuffer.AddBuffer<CollectFixupReference>(jobIndex, stateEntity);
            fixupBuffer.CopyFrom(TransitionInfo.AsArray());
        }

        
        public static T GetRobotTrait<T>(StateData state, ActionKey action) where T : unmanaged, ITrait
        {
            return state.GetTraitOnObjectAtIndex<T>(action[k_RobotIndex]);
        }
        
        public static T GetDirtTrait<T>(StateData state, ActionKey action) where T : unmanaged, ITrait
        {
            return state.GetTraitOnObjectAtIndex<T>(action[k_DirtIndex]);
        }
        
    }

    public struct CollectFixupReference : IBufferElementData
    {
        internal StateTransitionInfoPair<StateEntityKey, ActionKey, StateTransitionInfo> TransitionInfo;
    }
}


