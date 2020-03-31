using System;
using Unity.Entities;

namespace Vella.Events
{
    /// <summary>
    /// System that is responsible for creating the EntityEventSystem. 
    /// It allows a package user to control where the <see cref="EntityEventSystem"/> is located.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public class EntityEventSystemLoader : SystemBase
    {
        //// Example Usage: 
        // public static class Initialization
        // {
        //     [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        //     public static void OnLoad()
        //     {
        //         EntityEventSystemLoader.SetParentType<PresentationSystemGroup>();
        //     }
        // }

        public static Type DefaultGroup { get; } = typeof(LateSimulationSystemGroup);

        private static Type ParentType = DefaultGroup;

        public static bool DisableAutoCreation;

        public static void SetParentType<T>() where T : ComponentSystemGroup => ParentType = typeof(T);

        protected override void OnCreate()
        {
            if(!DisableAutoCreation)
            {
                AddToGroup((ComponentSystemGroup)World.GetOrCreateSystem(ParentType));
            }
            Enabled = false;
            World.DestroySystem(this);
        }

        public static void AddToGroup<T>(T group) where T : ComponentSystemGroup
        {
            var eventSystem = group.World.CreateSystem<EntityEventSystem>();
            group.AddSystemToUpdateList(eventSystem);
        }

        protected override void OnUpdate() => throw new NotImplementedException();
    }

}

