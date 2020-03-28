using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.PerformanceTesting;
using UnityEngine;
using Vella.Events;
using Vella.Tests.Attributes;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

namespace Performance
{
    class IntegrationPerformanceTests : ECSTestsFixture
    {

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void CreateRandomEvents([Values(5, 25, 100, 1000)] int eventsPerArchetype, [Values(1, 10, 50, 100)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();

            var eMin = 1;
            var eMax = eventsPerArchetype;

            var aMin = 1;
            var aMax = archetypeCount;

            var group = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName($"Random entity counts between [{eMin}-{eMax}], and [{aMin}-{aMax}] archetypes"),
                SampleUnit = SampleUnit.Millisecond
            };

            var measurements = 25;
            var warmups = 5;
            var sw = new Stopwatch();


            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    system.EventsPerArchetype = UnityEngine.Random.Range(eMin, eMax);
                    system.ArchetypeCount = UnityEngine.Random.Range(aMin, aMax);
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.EventsPerArchetype = UnityEngine.Random.Range(eMin, eMax);
                    system.ArchetypeCount = UnityEngine.Random.Range(aMin, aMax);
                    system.Update();
                    sw.Restart();
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(group, sw.Elapsed.TotalMilliseconds);
                }
            }

        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void CompareToDefaultApproaches([Values(1, 25, 100, 250, 1000, 5000)] int eventsPerArchetype)
        {
            var system = World.GetOrCreateSystem<EntityEventSystem>();
            var components = new[]
            {
                ComponentType.ReadWrite<EntityEvent>(),
                ComponentType.ReadWrite<EcsTestData>(),
            };
            var archetype = Manager.CreateArchetype(components);
            var query = Manager.CreateEntityQuery(components);

            var groupA = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName($"Default"),
                SampleUnit = SampleUnit.Millisecond
            };

            var measurements = 25;
            var warmups = 5;

            var sw = new Stopwatch();
            var entities = new NativeList<Entity>(eventsPerArchetype, Allocator.Temp);
            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    Manager.CreateEntity(archetype, entities);
                    entities.Clear();
                }
                else
                {
                    sw.Restart();
                    Manager.DestroyEntity(query);
                    Manager.CreateEntity(archetype, entities);
                    for (int j = 0; j < entities.Length; j++)
                    {
                        Manager.SetComponentData<EcsTestData>(entities[j], default);
                    }
                    sw.Stop();
                    Measure.Custom(groupA, sw.Elapsed.TotalMilliseconds);
                }
            }

            var queue = system.GetQueue<EcsTestData>();
            var groupB = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName("ECS Events"),
                SampleUnit = SampleUnit.Millisecond
            };

            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    queue.Enqueue(default(EcsTestData));
                    EventSystem.Update();
                }
                else
                {
                    sw.Restart();
                    for (int j = 0; j < entities.Length; j++)
                    {
                        queue.Enqueue(default(EcsTestData));
                    }
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(groupB, sw.Elapsed.TotalMilliseconds);
                }
            }

            var groupC = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName("EntityCommandBuffer"),
                SampleUnit = SampleUnit.Millisecond
            };

            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {

                }
                else
                {
                    var ecb = new EntityCommandBuffer(Allocator.Temp);
                    sw.Restart();
                    Manager.DestroyEntity(query);
                    for (int j = 0; j < entities.Length; j++)
                    {
                        var e = ecb.CreateEntity();
                        ecb.SetComponent<EcsTestData>(e, default);
                    }
                    ecb.Playback(Manager);
                    sw.Stop();
                    Measure.Custom(groupC, sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void UpdatePhaseBreakdown([Values(1, 1000, 10000)] int eventsPerArchetype, [Values(1, 25, 50)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();
            system.EventsPerArchetype = eventsPerArchetype;
            system.ArchetypeCount = archetypeCount;

            var processQueuedEventsGroup = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName("ProcessQueuedEvents", eventsPerArchetype, archetypeCount),
                SampleUnit = SampleUnit.Millisecond
            };

            var structuralChangesEventsGroup = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName("StructuralChanges", eventsPerArchetype, archetypeCount),
                SampleUnit = SampleUnit.Millisecond
            };

            var updateChunkCollectionsGroup = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName("UpdateChunkCollections", eventsPerArchetype, archetypeCount),
                SampleUnit = SampleUnit.Millisecond
            };

            var setComponentsGroup = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName("SetComponents", eventsPerArchetype, archetypeCount),
                SampleUnit = SampleUnit.Millisecond
            };

            var clearQueuesGroup = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName("ClearQueues", eventsPerArchetype, archetypeCount),
                SampleUnit = SampleUnit.Millisecond
            };

            var measurements = 25;
            var warmups = 5;
            var sw = new Stopwatch();
            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.Update();
                    sw.Restart();
                    EventSystem.ProcessQueuedEvents();
                    sw.Stop();
                    Measure.Custom(processQueuedEventsGroup, sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    EventSystem.Data.StructuralChanges.Apply();
                    Measure.Custom(structuralChangesEventsGroup, sw.Elapsed.TotalMilliseconds);
                    sw.Stop();

                    sw.Restart();
                    EventSystem.UpdateChunkCollections();
                    sw.Stop();
                    Measure.Custom(updateChunkCollectionsGroup, sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    EventSystem.SetComponents();
                    sw.Stop();
                    Measure.Custom(setComponentsGroup, sw.Elapsed.TotalMilliseconds);

                    sw.Restart();
                    EventSystem.ClearQueues();
                    sw.Stop();
                    Measure.Custom(clearQueuesGroup, sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void CreateEvents([Values(1, 10, 100, 1000, 10000)] int eventsPerArchetype, [Values(1, 10, 50, 100)] int archetypeCount)
        {
            var system = Manager.World.GetOrCreateSystem<LoadTestSystem>();
            system.EventsPerArchetype = eventsPerArchetype;
            system.ArchetypeCount = archetypeCount;

            var group = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName("Creating events that were queued on main thread", eventsPerArchetype, archetypeCount),
                SampleUnit = SampleUnit.Millisecond
            };

            var measurements = 25;
            var warmups = 5;
            var sw = new Stopwatch();
            for (int i = 0; i < measurements; i++)
            {
                if(i < warmups)
                {
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.Update();
                    sw.Restart();
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(group, sw.Elapsed.TotalMilliseconds);
                }
            }

            system.IsParallel = true;
            system.ThreadsPerArchetype = 4;

            // It matters whether the events are queued in a single thread or multiple 
            // because of the way data is stored and read. It will effect set component time, 
            // counting how many entities are queued, etc.

            var group2 = new SampleGroupDefinition 
            {
                AggregationType = AggregationType.Average,
                Name = GetName("Creating events that were queued in parallel", eventsPerArchetype, archetypeCount),
                SampleUnit = SampleUnit.Millisecond
            };

            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.Update();
                    sw.Restart();
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(group2, sw.Elapsed.TotalMilliseconds);
                }
            }
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void CreateBufferEvents([Values(1, 10, 100, 1000)] int eventCount, [Values(1, 10, 100, 1000, 10000)] int bufferLength)
        {
            var system = Manager.World.GetOrCreateSystem<BufferEventFromJobsWithCodeSystem>();

            system.EventCount = eventCount;
            system.BufferElementCount = bufferLength;

            var group = new SampleGroupDefinition
            {
                AggregationType = AggregationType.Average,
                Name = GetName($"Creating {eventCount} events with {bufferLength} length buffers, queued from main thread "),
                SampleUnit = SampleUnit.Millisecond
            };

            var measurements = 25;
            var warmups = 5;
            var sw = new Stopwatch();
            for (int i = 0; i < measurements; i++)
            {
                if (i < warmups)
                {
                    system.Update();
                    EventSystem.Update();
                }
                else
                {
                    system.Update();
                    sw.Restart();
                    EventSystem.Update();
                    sw.Stop();
                    Measure.Custom(group, sw.Elapsed.TotalMilliseconds);
                }
            }
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

        private string GetName([CallerMemberName] string memberName = "Method", int eventsPerArchetype = 0, int archetypeCount = 0)
        {
            string parenStr = "";
            if (eventsPerArchetype > 0 && archetypeCount > 0)
            {
                parenStr = $" (EventsPerAchetype={eventsPerArchetype}, Archetypes={archetypeCount} Total={eventsPerArchetype * archetypeCount})";
            }
            return $"{memberName}{parenStr}";
        }

        [DisableAutoCreation]
        public class LoadTestSystem : SystemBase
        {
            private EventQueue _queue;

            private static NativeArray<TypeManager.TypeInfo> _componentTypeInfos;
            private UnityEngine.Random _random;
            private int archetypeCount = 1;
            private EntityEventSystem _eventSystem;

            public bool IsCreated { get; private set; }

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
                        //var idx = _random.Next(0, _componentTypeInfos.Length - 1);
                        var info = _componentTypeInfos[math.min(i, _componentTypeInfos.Length - 1)];
                        var queue = _eventSystem.GetQueue(info);
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
                    // Store all the component types so that we can simiulate events with them.
                    // * Avoid ChunkHeader - EntityManagerDebug.CheckInternalConsistency() will fail tests if ChunkHeader component is used.
                    // * Avoid all test data components or there's a chance to get 2x on the same entity.

                    var chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();

                    var types = TypeManager.GetAllTypes().Where(t => t.SizeInChunk > 1 && t.Category == TypeManager.TypeCategory.ComponentData
                        && !t.Type.FullName.Contains(nameof(Vella.Events)) && t.TypeIndex != chunkHeaderTypeIndex);

                    _componentTypeInfos = new NativeArray<TypeManager.TypeInfo>(types.ToArray(), Allocator.Persistent);
                }

                _random = new UnityEngine.Random();

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

                if (!IsParallel)
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
    }
}

