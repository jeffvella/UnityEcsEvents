using System;
using NUnit.Framework;
using Unity.Collections;
using Vella.Tests.Fixtures;
using Vella.Events;
using Vella.Tests.Data;
using Unity.Core;
using Unity.Entities;
using Vella.Tests.Attributes;

class EventSystemBasicTests : ECSTestsFixture
{

    [Test, TestCategory(TestCategory.Functionality)]
    unsafe public void CreatesAndDestroysEventEntity()
    {
        var query = Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());
        var eventSystem = Manager.World.GetOrCreateSystem<EntityEventSystem>();

        eventSystem.Enqueue(EventComponentData);
        eventSystem.Update();
        Assert.AreEqual(query.CalculateEntityCount(), 1);

        var entity = query.GetSingletonEntity();
        var data = Manager.GetComponentData<EcsTestData>(entity);
        Assert.AreEqual(EventComponentData.value, data.value);

        eventSystem.Update();
        Assert.AreEqual(query.CalculateEntityCount(), 0);
    }

    [DisableAutoCreation]
    public class EventFromFieldQueueSystem : SystemBase
    {
        private EventQueue<EcsTestData> _queue;

        protected override void OnCreate()
        {
            _queue = World.GetOrCreateSystem<EntityEventSystem>().GetQueue<EcsTestData>();
        }

        protected override void OnUpdate()
        {
            _queue.Enqueue(EventComponentData);
        }
    }

    [Test, TestCategory(TestCategory.Functionality)]
    unsafe public void EventFromFieldQueue()
    {
        var query = Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());
        var eventSystem = Manager.World.GetOrCreateSystem<EntityEventSystem>();
        var queueSystem = Manager.World.GetOrCreateSystem<EventFromFieldQueueSystem>();

        queueSystem.Update();
        eventSystem.Update();

        Assert.AreEqual(query.CalculateEntityCount(), 1);

        var entity = query.GetSingletonEntity();
        var data = Manager.GetComponentData<EcsTestData>(entity);
        Assert.AreEqual(data.value, EventComponentData.value);
    }

    [DisableAutoCreation]
    public class EventFromJobsWithCodeSystem : SystemBase
    {
        private EventQueue<EcsTestData> _queue;

        protected override void OnCreate()
        {
            _queue = World.GetOrCreateSystem<EntityEventSystem>().GetQueue<EcsTestData>();
        }

        protected override void OnUpdate()
        {
            var queue = _queue;

            Job.WithCode(() =>
            {
                queue.Enqueue(EventComponentData);

            }).Run();
        }
    }

    [Test, TestCategory(TestCategory.Functionality)]
    unsafe public void EventFromJobsWithCode()
    {
        var query = Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());
        var eventSystem = Manager.World.GetOrCreateSystem<EntityEventSystem>();
        var queueSystem = Manager.World.GetOrCreateSystem<EventFromJobsWithCodeSystem>();

        queueSystem.Update();
        eventSystem.Update();

        Assert.AreEqual(query.CalculateEntityCount(), 1);

        var entity = query.GetSingletonEntity();
        var data = Manager.GetComponentData<EcsTestData>(entity);
        Assert.AreEqual(data.value, EventComponentData.value);
    }

}
