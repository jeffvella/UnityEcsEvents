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

class UnsafeEntityManagerTests : ECSTestsFixture
{
    [Test, TestCategory(TestCategory.Integrity), Performance]
    unsafe public void CreateEntities()
    {
        var warmupTimes = 10;
        var measureTimes = 20;
        var entitiesPerMeasurement = 20;
        var expectedTotal = (measureTimes + warmupTimes) * entitiesPerMeasurement;

        var uem = Manager.Unsafe; //new UnsafeEntityManager(Manager);
        var component = ComponentType.ReadWrite<EcsTestData>();
        var archetype = Manager.CreateArchetype(component);
        var query = Manager.CreateEntityQuery(component);
        var entities = new NativeArray<Entity>(expectedTotal, Allocator.TempJob);

        Measure.Method(() =>
        {
            // Note the performance will change depending on if burst compilation is enabled
            
            uem.CreateEntity(archetype, entities, entitiesPerMeasurement);

            //StructuralChangeProxy.TestSharedData.Shared.Data.CreateEntity(archetype, entities, entitiesPerMeasurement);
        })
        .WarmupCount(warmupTimes)
        .MeasurementCount(measureTimes)
        .Run();

        Assert.AreEqual(expectedTotal, query.CalculateEntityCount());
        entities.Dispose();
    }
}
 