using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Vella.Events
{
    public interface IEventObserver<T> where T : unmanaged, IComponentData
    {
        void OnEvent(T e);
    }

    /// <summary>
    /// Allows GameObjects to easily be notified of ECS world events.
    /// </summary>
    public class EventRouter : MonoBehaviour
    {
        // todo: replacement for CreateArchetypeChunkArray that doesn't allocate.
        // todo: burst compiled version able to call managed methods.

        protected EventDispatcher _dispatcherSystem;
        protected EntityEventSystem _eventSystem;
        protected World _world;

        /// <summary>
        /// Triggers an ECS event
        /// </summary>
        /// <typeparam name="T">the type of event</typeparam>
        /// <param name="eventData">the data to be set on the event</param>
        public void FireEvent<T>(T eventData) where T : struct, IComponentData => _eventSystem.Enqueue(eventData);

        private void Awake()
        {
            _world = World.DefaultGameObjectInjectionWorld;
            _dispatcherSystem = _world.GetOrCreateSystem<EventDispatcher>();
            _eventSystem = _world.GetOrCreateSystem<EntityEventSystem>();
        }

        /// <summary>
        /// Subscribes an observer to receive event notifications.
        /// </summary>
        /// <typeparam name="T">the type of event</typeparam>
        /// <param name="observer">the object that will receive events</param>
        public void Subscribe<T>(IEventObserver<T> observer = default) where T : unmanaged, IComponentData
        {
            int typeIndex = TypeManager.GetTypeIndex<T>();
            var invoker = new DelegateInvoker<T> { Handler = observer };
            _dispatcherSystem.Add(typeIndex, invoker);
        }

        /// <summary>
        /// Removes an observer so that it stop receiving events and is no longer referenced.
        /// </summary>
        /// <typeparam name="T">the type of event</typeparam>
        /// <param name="observer">the object that will receive events</param>
        public void Unsubscribe<T>(IEventObserver<T> handler = default) where T : unmanaged, IComponentData
        {
            int typeIndex = TypeManager.GetTypeIndex<T>();
            var invoker = new DelegateInvoker<T> { Handler = handler };
            _dispatcherSystem.Remove(typeIndex, invoker);
        }

        public unsafe interface IDelegateInvoker
        {
            void Execute(void* ptr);

            void ExecuteDefault();
        }

        public unsafe struct DelegateInvoker<T> : IDelegateInvoker, IEquatable<DelegateInvoker<T>> where T : unmanaged, IComponentData
        {
            public IEventObserver<T> Handler;

            public void Execute(void* ptr) => Handler.OnEvent(*(T*)ptr);

            public void ExecuteDefault() => Handler.OnEvent(default);

            public bool Equals(DelegateInvoker<T> other) => Handler.GetHashCode() == other.Handler.GetHashCode();

            public override int GetHashCode()
            {
                int hash = 13;
                hash = hash * 7 + Handler.GetHashCode();
                hash = hash * 7 + typeof(T).GetHashCode();
                return hash;
            }
        }

        public class EventDispatcher : SystemBase
        {
            private Dictionary<int, List<IDelegateInvoker>> _actions;
            private EntityQuery _query;

            public EventDispatcher() => _actions = new Dictionary<int, List<IDelegateInvoker>>();

            protected override void OnCreate()
            {
                _actions = new Dictionary<int, List<IDelegateInvoker>>();
                _query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<EntityEvent>());

                RequireForUpdate(_query);
            }

            internal void Add<T>(int typeIndex, DelegateInvoker<T> invoker) where T : unmanaged, IComponentData
            {
                if (!_actions.ContainsKey(typeIndex))
                {
                    _actions[typeIndex] = new List<IDelegateInvoker> { invoker };
                }
                else
                {
                    _actions[typeIndex].Add(invoker);
                }
            }

            internal void Remove<T>(int typeIndex, DelegateInvoker<T> invoker) where T : unmanaged, IComponentData
            {
                if (_actions.ContainsKey(typeIndex))
                {
                    var invokers = _actions[typeIndex];
                    if (invokers == null || invokers.Count == 0)
                        return;

                    invokers.Remove(invoker);
                }
            }

            protected override unsafe void OnUpdate()
            {
                if (_query.IsEmptyIgnoreFilter)
                    return;

                var chunks = _query.CreateArchetypeChunkArray(Allocator.TempJob);
                var uem = EntityManager.Unsafe;

                foreach (var chunk in chunks)
                {
                    int componentTypeIndex = uem.GetComponentPtr<EntityEvent>(chunk)->ComponentTypeIndex;
                    if (componentTypeIndex != 0)
                    {
                        if (_actions.TryGetValue(componentTypeIndex, out var list))
                        {
                            byte* componentsPtr = uem.GetComponentPtr(chunk, componentTypeIndex);
                            var typeInfo = TypeManager.GetTypeInfo(componentTypeIndex);

                            for (int i = 0; i < list.Count; i++)
                            {
                                for (int j = 0; j < chunk.Count; j++)
                                {
                                    if (typeInfo.IsZeroSized)
                                        list[i].ExecuteDefault();
                                    else
                                        list[i].Execute(componentsPtr + j * typeInfo.SizeInChunk);
                                }
                            }
                        }
                    }
                }

                chunks.Dispose();
            }
        }
    }
}
