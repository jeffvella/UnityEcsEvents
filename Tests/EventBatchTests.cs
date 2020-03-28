using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;
using Vella.Events;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;


public class EventBatchTests : ECSTestsFixture
{
    [Test]
    public void BatchArchetypesContainTypes()
    {
        var meta = ComponentType.ReadWrite<EntityEvent>();

        // Component Only

        var batch = EventBatch.Create<EcsTestData>(Manager, meta, 0, Allocator.Temp);

        var activeTypes = batch.Archetype.GetComponentTypes().ToArray();

        Assert.Contains(meta, activeTypes);
        Assert.Contains(ComponentType.ReadWrite<EcsTestData>(), activeTypes);

        var inactiveTypes = batch.InactiveArchetype.GetComponentTypes().ToArray();

        Assert.Contains(meta, inactiveTypes);
        Assert.Contains(ComponentType.ReadWrite<EcsTestData>(), inactiveTypes);
        Assert.Contains(ComponentType.ReadWrite<Disabled>(), inactiveTypes);

        // Component + Buffer

        batch = EventBatch.Create<EcsTestData, EcsIntElement >(Manager, meta, 0, Allocator.Temp);

        activeTypes = batch.Archetype.GetComponentTypes().ToArray();

        Assert.Contains(meta, activeTypes);
        Assert.Contains(ComponentType.ReadWrite<EcsTestData>(), activeTypes);
        Assert.Contains(ComponentType.ReadWrite<EcsIntElement>(), activeTypes);
        Assert.Contains(ComponentType.ReadWrite<BufferLink>(), activeTypes);

        inactiveTypes = batch.InactiveArchetype.GetComponentTypes().ToArray();

        Assert.Contains(meta, inactiveTypes);
        Assert.Contains(ComponentType.ReadWrite<EcsTestData>(), inactiveTypes);
        Assert.Contains(ComponentType.ReadWrite<EcsIntElement>(), activeTypes);
        Assert.Contains(ComponentType.ReadWrite<BufferLink>(), activeTypes);
        Assert.Contains(ComponentType.ReadWrite<Disabled>(), inactiveTypes);
    }

    [Test]
    public void CreatesPoolInactives()
    {
        var meta = ComponentType.ReadWrite<EntityEvent>();

        // Component Only

        var batch = EventBatch.Create<EcsTestData>(Manager, meta, 1, Allocator.Temp);
        var activeQuery = Manager.CreateEntityQuery(batch.Archetype.GetComponentTypes().ToArray());
        var inactiveQuery = Manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = batch.Archetype.GetComponentTypes().ToArray(),
            Options = EntityQueryOptions.IncludeDisabled
        });

        Assert.Zero(activeQuery.CalculateEntityCount());
        Assert.AreEqual(1, inactiveQuery.CalculateEntityCount());

        Manager.DestroyEntity(inactiveQuery);

        batch = EventBatch.Create<EcsTestData>(Manager, meta, 12345, Allocator.Temp);
        activeQuery = Manager.CreateEntityQuery(batch.Archetype.GetComponentTypes().ToArray());
        inactiveQuery = Manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = batch.Archetype.GetComponentTypes().ToArray(),
            Options = EntityQueryOptions.IncludeDisabled
        });

        Assert.Zero(activeQuery.CalculateEntityCount());
        Assert.AreEqual(12345, inactiveQuery.CalculateEntityCount());

        Manager.DestroyEntity(inactiveQuery);

        // Component + Buffer

        batch = EventBatch.Create<EcsTestData, EcsIntElement>(Manager, meta, 100, Allocator.Temp);
        activeQuery = Manager.CreateEntityQuery(batch.Archetype.GetComponentTypes().ToArray());
        inactiveQuery = Manager.CreateEntityQuery(new EntityQueryDesc
        {
            All = batch.Archetype.GetComponentTypes().ToArray(),
            Options = EntityQueryOptions.IncludeDisabled
        });

        Assert.Zero(activeQuery.CalculateEntityCount());
        Assert.AreEqual(100, inactiveQuery.CalculateEntityCount());
    }

    [Test]
    public void HasBufferForBufferBatches()
    {

        var meta = ComponentType.ReadWrite<EntityEvent>();

        var componentBatch = EventBatch.Create<EcsTestData>(Manager, meta, 0, Allocator.Temp);
        Assert.IsFalse(componentBatch.HasBuffer);

        var bufferBatch = EventBatch.Create<EcsTestData, EcsIntElement>(Manager, meta, 0, Allocator.Temp);
        Assert.IsTrue(bufferBatch.HasBuffer);
    }

    // todo:
    // check offsets
    // check queue setup properly for both component and buffer

}
