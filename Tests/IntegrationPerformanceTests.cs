using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.PerformanceTesting;
using Vella.Events;
using Vella.Tests.Attributes;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

namespace Performance
{
    class IntegrationPerformanceTests : ECSTestsFixture
    {
        [DisableAutoCreation]
        public class LoadTestSystem : SystemBase
        {
            private EventQueue _queue;

            private static NativeArray<TypeManager.TypeInfo> _componentTypeInfos;
            private Random _random;
            private int archetypeCount = 1;
            private EntityEventSystem _eventSystem;

            public bool IsCreated { get; private set;}

            public int EventsPerArchetype { get; set; } = 1;

            public bool IsParallel { get; set; } = false;

            public int ThreadsPerArchetype { get; set; } = 1;


            public NativeList<EventQueue> _queues;

            public int ArchetypeCount
            {
                get => archetypeCount; internal
                set
                {
                    if (!IsCreated)
                        throw new InvalidOperationException();

                    archetypeCount = value;

                    // Pick a random block of IComponentData
                    _queues.Clear();

                    for (int i = 0; i < value; i++)
                    {
                        var idx = _random.Next(0, _componentTypeInfos.Length - 1);
                        var queue = _eventSystem.GetQueue(_componentTypeInfos[idx]);
                        _queues.Add(queue);
                    }
                }
            }

            protected override void OnCreate()
            {
                _eventSystem = World.GetOrCreateSystem<EntityEventSystem>();
                _queues = new NativeList<EventQueue>(Allocator.Persistent);

                if (!_componentTypeInfos.IsCreated)
                {
                    var types = TypeManager.GetAllTypes().Where(t => t.SizeInChunk > 1 &&  t.Category == TypeManager.TypeCategory.ComponentData 
                        && t.SizeInChunk > 0 
                        && !t.Type.FullName.Contains(nameof(Vella.Events))); // avoid 2x the same type on an entity exception

                    _componentTypeInfos = new NativeArray<TypeManager.TypeInfo>(types.ToArray(), Allocator.Persistent);
                }

                _random = new Random();

                IsCreated = true;
            }

            protected override void OnDestroy()
            {
                _componentTypeInfos.Dispose();
                _queues.Dispose();
            }

            protected unsafe override void OnUpdate()
            {
                var queues = _queues;
                var count = EventsPerArchetype;

                if(!IsParallel)
                {
                    Job.WithCode(() =>
                    {
                        for (int i = 0; i < queues.Length; i++)
                        {
                            var queue = queues[i];

                            for (int j = 0; j < count; j++)
                            {
                                queue.EnqueueDefault();
                            }
                        }

                    }).Run();
                }
                else if (ThreadsPerArchetype > 0 && ThreadsPerArchetype < JobsUtility.MaxJobThreadCount)
                {
                    var numPerThread = EventsPerArchetype / ThreadsPerArchetype;
                    var handles = new NativeArray<JobHandle>(queues.Length, Allocator.Temp);

                    for (int i = 0; i < queues.Length; i++)
                    {
                        handles[i] = new ThreadedJob
                        {
                            Events = _queues[i],

                        }.Schedule(ThreadsPerArchetype, numPerThread);
                    }

                    JobHandle.CombineDependencies(handles).Complete();
                }
            }

            [BurstCompile(CompileSynchronously = true)]
            public struct ThreadedJob : IJobParallelFor
            {
                public EventQueue Events;

                public void Execute(int index)
                {
                    Events.EnqueueDefault();
                }
            }
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void OneComponentEventQueue()
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();

            Measure.Method(() =>
            {
                system.Update();

            })
            .CleanUp(() =>
            {
                EventSystem.Update();
            })
            .WarmupCount(2)
            .IterationsPerMeasurement(1)
            .Run();
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void OneComponentEventCreate() 
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();

            Measure.Method(() =>
            {
                EventSystem.Update();
            })
            .SetUp(() =>
            {
                system.Update();
            })
            .CleanUp(() =>
            {

            })
            .WarmupCount(2)
            .IterationsPerMeasurement(1)
            .Run();
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void QueueAndCreateHighComponents([Values(1, 10, 100, 1000, 10000)] int eventsPerarchetype, [Values(1, 10)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();

            system.EventsPerArchetype = eventsPerarchetype;
            system.ArchetypeCount = archetypeCount;

            Measure.Method(() =>
            {
                system.Update();
                EventSystem.Update();
            })
            .SetUp(() =>
            {

            })
            .CleanUp(() =>
            {

            })
            .WarmupCount(5)
            .IterationsPerMeasurement(1)
            .Run();
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void CreateHighComponents([Values(1, 10, 100, 1000, 10000)] int eventsPerarchetype, [Values(1, 10, 50, 500)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();
            system.EventsPerArchetype = eventsPerarchetype;
            system.ArchetypeCount = archetypeCount;
            system.Update();

            Measure.Method(() =>
            {
                EventSystem.Update();
            })
            .MeasurementCount(1)
            .WarmupCount(0)
            .IterationsPerMeasurement(1)
            .Run();
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void QueueAndCreateHighArchetypes([Values(1, 10)] int eventsPerarchetype, [Values(1, 5, 15, 50, 100)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();
            system.EventsPerArchetype = eventsPerarchetype;
            system.ArchetypeCount = archetypeCount;

            Measure.Method(() =>
            {
                system.Update();
                EventSystem.Update();
            })
            .SetUp(() =>
            {

            })
            .CleanUp(() =>
            {

            })
            .WarmupCount(5)
            .IterationsPerMeasurement(1)
            .Run();
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void QueueAndCreateThreaded([Values(1, 500, 25000, 100000, 500000)] int eventsPerarchetype, [Values(1, 2, 5, 8)] int threadCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();

            system.EventsPerArchetype = eventsPerarchetype;
            system.ArchetypeCount = 1;
            system.IsParallel = true;
            system.ThreadsPerArchetype = threadCount;

            Measure.Method(() =>
            {
                system.Update();
                EventSystem.Update();
            })
            .SetUp(() =>
            {

            })
            .CleanUp(() =>
            {

            })
            .WarmupCount(5)
            .IterationsPerMeasurement(1)
            .Run();
        }



        [DisableAutoCreation]
        public class BufferEventFromJobsWithCodeSystem : SystemBase
        {
            private EventQueue<EcsTestData, EcsIntElement> _queue;

            public int EventCount { get; internal set; } = 1;

            public int BufferElementCount { get; internal set; } = 1;

            protected override void OnCreate()
            {
                _queue = World.GetOrCreateSystem<EntityEventSystem>().GetQueue<EcsTestData, EcsIntElement>();
            }

            protected unsafe override void OnUpdate()
            {
                var queue = _queue;
                var componentCount = EventCount;
                var bufferElementCount = BufferElementCount;

                Job.WithCode(() =>
                {
                    var bufferPtr = stackalloc EcsIntElement[bufferElementCount];
                    for (int i = 0; i < componentCount; i++)
                    {
                        queue.Enqueue(EventComponentData, bufferPtr, bufferElementCount);
                    }

                }).Run();
            }
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void QueueAndCreateBufferEvents([Values(1, 10, 100, 1000)] int eventCount, [Values(1, 10, 100, 1000, 10000)] int bufferLength)
        {
            var system = Manager.World.GetOrCreateSystem<BufferEventFromJobsWithCodeSystem>();

            system.EventCount = eventCount;
            system.BufferElementCount = bufferLength;

            Measure.Method(() =>
            {
                system.Update();
                EventSystem.Update();
            })
            .SetUp(() =>
            {

            })
            .CleanUp(() =>
            {

            })
            .WarmupCount(5)
            .IterationsPerMeasurement(1)
            .Run();
        }


    }
}

