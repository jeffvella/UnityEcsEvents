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

class ProxyTests : ECSTestsFixture
{
    //struct CompA : IComponentData
    //{

    //}

    //class CreateRef : SystemBase
    //{
    //    EndSimulationEntityCommandBufferSystem _barrier;

    //    protected override void OnCreate()
    //    {
    //        _barrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    //    }

    //    protected override void OnUpdate()
    //    {
    //        var ecb = _barrier.CreateCommandBuffer().ToConcurrent();
    //        var arch = EntityManager.CreateArchetype(typeof(CompA));

    //        Entities.ForEach((int entityInQueryIndex, Entity e, ref DynamicBuffer<LinkedEntityGroup> group) =>
    //        {
    //            var newA = ecb.CreateEntity(entityInQueryIndex, arch);
    //            group.Add(newA);

    //        }).ScheduleParallel();

    //        _barrier.AddJobHandleForProducer(Dependency);
    //    }
    //}


    //[Test]
    //public void TestRef()
    //{
    //    var refSystem = World.GetOrCreateSystem<CreateRef>();
    //    var barrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
    //    var em = Manager;

    //    var groupEntity = em.CreateEntity(typeof(LinkedEntityGroup));

    //    refSystem.Update();
    //    barrier.Update();

    //    em.CompleteAllJobs();

    //    var buffer = em.GetBuffer<LinkedEntityGroup>(groupEntity);
    //    var aQuery = em.CreateEntityQuery(typeof(CompA));

    //    Assert.AreEqual(aQuery.GetSingletonEntity(), buffer[0].Value);
    //}


    public struct MyComponent : IComponentData
    {
        public Entity Reference;
        public int Value;
    }

    public struct MyElement : IBufferElementData
    {
        public Entity Reference;
        public int Value;
    }

    public class MySystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var entity = ecb.CreateEntity();
            ecb.AddComponent(entity, new MyComponent
            {
                Reference = entity,
                Value = 42,
            });
            var buffer = ecb.AddBuffer<MyElement>(entity);
            buffer.Add(new MyElement
            {
                Reference = entity,
                Value = 41
            });
            ecb.Playback(EntityManager);

            var createdEntity = GetSingletonEntity<MyComponent>();
            var createdComponent = GetSingleton<MyComponent>();

            Assert.AreEqual(createdComponent.Reference, createdEntity);
            Assert.AreNotEqual(createdEntity, entity);
            Assert.AreEqual(createdComponent.Value, 42);

            var createdBuffer = EntityManager.GetBuffer<MyElement>(createdEntity);
            
            Assert.AreEqual(createdBuffer[0].Reference, createdEntity);
            Assert.AreEqual(createdBuffer[0].Value, 41);
        }
    }

    [Test]
    unsafe public void ECBReferenceTest()
    {
        World.GetOrCreateSystem<MySystem>().Update();
    }


    public class TestSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer();
            var entity = ecb.CreateEntity();

        }
    }

    [Test, TestCategory(TestCategory.Integrity), Performance]
    unsafe public void StructuralChangeCreateEntities()
    {
        var warmupTimes = 10;
        var measureTimes = 20;
        var entitiesPerMeasurement = 20;
        var expectedTotal = (measureTimes + warmupTimes) * entitiesPerMeasurement;

        var uem = new UnsafeEntityManager(Manager);
        var component = ComponentType.ReadWrite<EcsTestData>();
        var archetype = Manager.CreateArchetype(component);
        var query = Manager.CreateEntityQuery(component);
        var entities = new NativeArray<Entity>(entitiesPerMeasurement, Allocator.TempJob);

        Measure.Method(() =>
        {
            uem.CreateEntities(archetype, entities);
        })
        .WarmupCount(warmupTimes)
        .MeasurementCount(measureTimes)
        .Run();

        Assert.AreEqual(expectedTotal, query.CalculateEntityCount());
        entities.Dispose();
    }


    [Test, TestCategory(TestCategory.Integrity)]
    unsafe public void BufferHeaderProxy()
    {
        var unityType = FindType("Unity.Entities.BufferHeader");
        AssertTypeSizesAreEqual<BufferHeaderProxy>(unityType);
        AssertInstanceBytesAreEqual<BufferHeaderProxy>(unityType);
        AssertFieldsAreEqual<BufferHeaderProxy>(unityType);
    }

    [Test, TestCategory(TestCategory.Integrity)]
    unsafe public void EntityArchetypeProxy()
    {
        var unityType = typeof(EntityArchetype);
        AssertTypeSizesAreEqual<VeryUnsafeExtensions.EntityArchetypeProxy>(unityType);
        AssertInstanceBytesAreEqual<VeryUnsafeExtensions.EntityArchetypeProxy>(unityType);

        var flags = FieldComparisonFlags.AllowVoidPointerReplacement | FieldComparisonFlags.IgnorePointers;
        AssertFieldsAreEqual<VeryUnsafeExtensions.EntityArchetypeProxy>(unityType, flags);
    }

    [Test, TestCategory(TestCategory.Integrity)]
    unsafe public void ArchetypeChunkProxy()
    {
        var unityType = typeof(ArchetypeChunk);
        AssertTypeSizesAreEqual<VeryUnsafeExtensions.ArchetypeChunkProxy>(unityType);
        AssertInstanceBytesAreEqual<VeryUnsafeExtensions.ArchetypeChunkProxy>(unityType);

        var flags = FieldComparisonFlags.AllowVoidPointerReplacement;
        AssertFieldsAreEqual<VeryUnsafeExtensions.ArchetypeChunkProxy>(unityType, flags);
    }
}
