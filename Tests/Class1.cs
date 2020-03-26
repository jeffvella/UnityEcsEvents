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

public class EntityManagerSequential : EntityPerformanceTestFixture
{
    [Test, Performance]
    public void ExactChunkSizedAmounts()
    {
        var system = m_World.GetOrCreateSystem<EntityEventSystem>();
        var queue = system.GetQueue<EcsTestData>();

        var measurement = 0;

        Measure.Method(() =>
        {
            measurement++;

            for (int i = 0; i < 1000; i++)
            {
                queue.Enqueue(new EcsTestData());
            }

            system.Update();
        })
        .MeasurementCount(1)
        .WarmupCount(0)
        .IterationsPerMeasurement(3)
        .Run();
    }

    [Test]
    public void DecreasingChunkSizedAmounts()
    {
        var system = m_World.GetOrCreateSystem<EntityEventSystem>();
        var queue = system.GetQueue<EcsTestData>();
        var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>());
        var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());

        var entitiesByMeasurement = new[]
        {
            2000,
            1000,
            0,
        };

        for (int i = 0; i < entitiesByMeasurement.Length; i++)
        {
            var entities = entitiesByMeasurement[i];
            for (int j = 0; j < entities; j++)
            {
                queue.Enqueue(new EcsTestData());
            }
            system.Update();

            Assert.AreEqual(query.CalculateEntityCount(),entities);

            var expectedChunks = entities / archetype.ChunkCapacity + ((entities % archetype.ChunkCapacity == 0) ? 0 : 1);
            Assert.AreEqual(expectedChunks, query.CalculateChunkCount());
        }
    }

    [Test]
    public void IncreasingChunkSizedAmounts()
    {
        var system = m_World.GetOrCreateSystem<EntityEventSystem>();
        var queue = system.GetQueue<EcsTestData>();
        var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>());
        var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());

        var entitiesByMeasurement = new[]
        {
            1000,
            3000,
            5000,
        };

        for (int i = 0; i < entitiesByMeasurement.Length; i++)
        {
            var entities = entitiesByMeasurement[i];
            for (int j = 0; j < entities; j++)
            {
                queue.Enqueue(new EcsTestData());
            }
            system.Update();

            Assert.AreEqual(query.CalculateEntityCount(), entities);

            var expectedChunks = entities / archetype.ChunkCapacity + ((entities % archetype.ChunkCapacity == 0) ? 0 : 1);
            Assert.AreEqual(expectedChunks, query.CalculateChunkCount());
        }
    }

    [Test]
    public void IrregularAmounts()
    {
        var system = m_World.GetOrCreateSystem<EntityEventSystem>();
        var queue = system.GetQueue<EcsTestData>();
        var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>());
        var query = m_Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());

        var entitiesByMeasurement = new[]
        {
            1,
            3,
            7,
            1000,
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

        for (int i = 0; i < entitiesByMeasurement.Length; i++)
        {
            var entities = entitiesByMeasurement[i];
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


