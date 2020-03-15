using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
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
        public class ComponentEventFromJobsWithCodeSystem : SystemBase
        {
            private EventQueue<EcsTestData> _queue;

            public int EventCount { get; internal set; } = 1;

            protected override void OnCreate()
            {
                _queue = World.GetOrCreateSystem<EntityEventSystem>().GetQueue<EcsTestData>();
            }

            protected override void OnUpdate()
            {
                var queue = _queue;
                var count = EventCount;

                Job.WithCode(() =>
                {
                    for (int i = 0; i < count; i++)
                    {
                        queue.Enqueue(EventComponentData);
                    }

                }).Run();
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
                _queue = World.GetOrCreateSystem<EntityEventSystem>().GetQueue<EcsTestData,EcsIntElement>();
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
        public void QueueComponentsJobWithCode()
        {
            var queueSystem = Manager.World.GetOrCreateSystem<ComponentEventFromJobsWithCodeSystem>();

            Measure.Method(() =>
            {
                queueSystem.Update();

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
        public void CreateComponentEvent() 
        {
            var queueSystem = Manager.World.GetOrCreateSystem<ComponentEventFromJobsWithCodeSystem>();

            Measure.Method(() =>
            {
                EventSystem.Update();
            })
            .SetUp(() =>
            {
                queueSystem.Update();
            })
            .CleanUp(() =>
            {

            })
            .WarmupCount(2)
            .IterationsPerMeasurement(1)
            .Run();
        }

        [Test, Performance, TestCategory(TestCategory.Performance)]
        public void QueueAndCreateComponentEvents([Values(1, 10, 100, 1000, 10000)] int eventCount)
        {
            var queueSystem = Manager.World.GetOrCreateSystem<ComponentEventFromJobsWithCodeSystem>();

            queueSystem.EventCount = eventCount;

            Measure.Method(() =>
            {
                queueSystem.Update();
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
        public void QueueAndCreateBufferEvents([Values(1, 10, 100, 1000)] int eventCount, [Values(1, 10, 100, 1000)] int bufferLength)
        {
            var queueSystem = Manager.World.GetOrCreateSystem<BufferEventFromJobsWithCodeSystem>();

            queueSystem.EventCount = eventCount;
            queueSystem.BufferElementCount = bufferLength;

            Measure.Method(() =>
            {
                queueSystem.Update();
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

