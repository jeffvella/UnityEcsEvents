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

public class NewTests : EntityPerformanceTestFixture
{
    [Test]
    public void CachedChunkDebug()
    {
        var system = m_World.GetOrCreateSystem<EntityEventSystem>();
        var queue = system.GetQueue<EcsTestData>();

        Measure.Method(() =>
        {
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

    public class TestSystem1 : SystemBase
    {
        protected override void OnUpdate()
        {
            var cap = 1000;
            var required = 10000;
            var result = 0;

            Job.WithCode(() =>
            {
                for (int i = 0; i < 100000; i++)
                {
                    if (cap != required)
                        result = 1 + (required / cap);
                    else
                        result = 1;
                }

            }).Run();
        }
    }

    [Test, Performance]
    public void DivideVsBranch1()
    {

        var system = m_World.GetOrCreateSystem<TestSystem1>();
        Measure.Method(() =>
        {
            system.Update();
        })
        .MeasurementCount(25)
        .WarmupCount(5)
        .Definition("Branch", SampleUnit.Nanosecond)
        .IterationsPerMeasurement(1)
        .Run();
    }

    public class TestSystem2 : SystemBase
    {
        protected override void OnUpdate()
        {
            var cap = 1000;
            var required = 10000;
            var result = 0;

            Job.WithCode(() =>
            {
                for (int i = 0; i < 100000; i++)
                {
                    result = cap % required / cap + required / cap;
                }

            }).Run();
        }
    }

    [Test, Performance]
    public void DivideVsBranch2()
    {
        var system = m_World.GetOrCreateSystem<TestSystem2>();
        Measure.Method(() =>
        {
            system.Update();
        })
        .MeasurementCount(25)
        .WarmupCount(5)
        .Definition("Divide", SampleUnit.Nanosecond)
        .IterationsPerMeasurement(1)
        .Run();


    }
}


