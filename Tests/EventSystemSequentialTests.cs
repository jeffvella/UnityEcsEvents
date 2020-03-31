using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.PerformanceTesting;
using Vella.Events;
using Vella.Tests.Attributes;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

public class EventSystemSequentialTests : EntityPerformanceTestFixture
{
    [Test]
    public void ExactChunkSizedAmounts()
    {
        Setup(out var system, out var archetype, out var query);
        var queue = system.GetQueue<EcsTestData>();

        var entityCounts = new[]
        {
            archetype.ChunkCapacity,
            archetype.ChunkCapacity,
            archetype.ChunkCapacity,
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    [Test]
    public void DecreasingChunkSizedAmounts()
    {
        Setup(out var system, out var archetype, out var query);
        var queue = system.GetQueue<EcsTestData>();

        var entityCounts = new[]
        {
            archetype.ChunkCapacity * 3,
            archetype.ChunkCapacity * 2,
            archetype.ChunkCapacity,
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    [Test]
    public void IncreasingChunkSizedAmounts()
    {
        Setup(out var system, out var archetype, out var query);
        var queue = system.GetQueue<EcsTestData>();

        var entityCounts = new[]
        {
            archetype.ChunkCapacity,
            archetype.ChunkCapacity * 2,
            archetype.ChunkCapacity * 5,
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    [Test]
    public void CurvedChunkSizedAmounts()
    {
        Setup(out var system, out var archetype, out var query);
        var queue = system.GetQueue<EcsTestData>();

        var entityCounts = new[]
        {
            archetype.ChunkCapacity,
            archetype.ChunkCapacity * 2,
            archetype.ChunkCapacity * 5,
            archetype.ChunkCapacity * 2,
            archetype.ChunkCapacity,
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    [Test]
    public void ShortcutFullChunksPlusPartial()
    {
        Setup(out var system, out var archetype, out var query);
        var queue = system.GetQueue<EcsTestData>();

        var entityCounts = new[]
        {
            archetype.ChunkCapacity * 5 + 1,
            archetype.ChunkCapacity + 1,
            // Should now be 1 active, 4 inactive full chunks.
            archetype.ChunkCapacity * 8 + 1 
            // for 8 chunks, 1 should be kept, 4 should be converted, 3 should be created
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    [Test]
    public void IrregularAmounts()
    {
        Setup(out var system, out var archetype, out var query);
        var queue = system.GetQueue<EcsTestData>();

        var entityCounts = new[]
        {
            1,
            3,
            7,
            1001,
            3,
            2000,
            1525,
            5,
            30,
            0,
            0,
            5,
            1,
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    [Test]
    public void OnePartialFromPartials()
    {
        Setup(out var system, out var archetype, out var query);

        var queue = system.GetQueue<EcsTestData>(1);

        var entityCounts = new[]
        {
            1,
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    [Test]
    public void ConvertFullInactiveChunks()
    {
        Setup(out var system, out var archetype, out var query);

        var queue = system.GetQueue<EcsTestData>(archetype.ChunkCapacity * 2);

        var entityCounts = new[]
        {
            archetype.ChunkCapacity,
            3,
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    [Test]
    public void CreateExcessFromInactives()
    {
        Setup(out var system, out var archetype, out var query);

        var queue = system.GetQueue<EcsTestData>(archetype.ChunkCapacity);

        var entityCounts = new[]
        {
            archetype.ChunkCapacity + archetype.ChunkCapacity / 2,
        };

        AssertEntitiesCreated(system, archetype, queue, query, entityCounts);
    }

    private void Setup(out EntityEventSystem system, out EntityArchetype archetype, out EntityQuery query)
    {
        system = m_World.GetOrCreateSystem<EntityEventSystem>();
        var components = new[]
        {
            ComponentType.ReadWrite<EntityEvent>(),
            ComponentType.ReadWrite<EcsTestData>(),
        };
        archetype = m_Manager.CreateArchetype(components);
        query = m_Manager.CreateEntityQuery(components);
    }

    private static void AssertEntitiesCreated(EntityEventSystem system, EntityArchetype archetype, EventQueue<EcsTestData> queue, EntityQuery query, int[] entityCounts)
    {
        for (int i = 0; i < entityCounts.Length; i++)
        {
            var entities = entityCounts[i];
            for (int j = 0; j < entities; j++)
            {
                queue.Enqueue(new EcsTestData());
            }
            system.Update();

            Assert.AreEqual(entities, query.CalculateEntityCount());

            var expectedChunks = entities / archetype.ChunkCapacity + ((entities % archetype.ChunkCapacity == 0) ? 0 : 1);
            Assert.AreEqual(expectedChunks, query.CalculateChunkCount());
        }
    }

}


