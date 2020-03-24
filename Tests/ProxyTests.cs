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
public static class VeryUnsafeExtensions
{
    public static unsafe bool Has(this ArchetypeChunk chunk, ComponentType type1, ComponentType type2, ComponentType type3)
    {
        var archetype = ((ArchetypeChunkImposter*)&chunk)->Chunk->Archetype;
        int* typeIndexes = archetype->Types;
        int typeCount = archetype->TypesCount;
        int found = 0;

        for (int i = 0; i != typeCount; i++)
        {
            var typeIndex = typeIndexes[i];
            if (typeIndex == type1.TypeIndex || typeIndex == type2.TypeIndex || typeIndex == type3.TypeIndex)
                found++;
        }
        return found == 3;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public unsafe struct ArchetypeChunkImposter
    {
        [FieldOffset(0)]
        public ChunkImposter* Chunk;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct ChunkImposter
    {
        [FieldOffset(0)]
        public ArchetypeImposter* Archetype;
    }

    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct ArchetypeImposter
    {
        [FieldOffset(ArchetypeOffsets._0120_ComponentTypeInArchetypePtr_Types_8)]
        [NativeDisableUnsafePtrRestriction]
        public int* Types;

        [FieldOffset(ArchetypeOffsets._0136_Int32_TypesCount_4)]
        public int TypesCount;

        [FieldOffset(ArchetypeOffsets._0160_Int32Ptr_Offsets_8)]
        [NativeDisableUnsafePtrRestriction]
        public int* Offsets;

        [FieldOffset(ArchetypeOffsets._0168_Int32Ptr_SizeOfs_8)]
        [NativeDisableUnsafePtrRestriction]
        public int* SizeOfs;
    }

    public struct ArchetypeOffsets // Entities: 0.8.0-preview.8
    {
        public const int _0000_ArchetypeChunkData_Chunks_p_8 = 0;
        public const int _0008_ArchetypeChunkData_Chunks_data_8 = 8;
        public const int _0016_ArchetypeChunkData_Chunks_Capacity_4 = 16;
        public const int _0020_ArchetypeChunkData_Chunks_Count_4 = 20;
        public const int _0024_ArchetypeChunkData_Chunks_SharedComponentCount_4 = 24;
        public const int _0028_ArchetypeChunkData_Chunks_EntityCountIndex_4 = 28;
        public const int _0032_ArchetypeChunkData_Chunks_Channels_4 = 32;
        public const int _0040_UnsafeChunkPtrList_ChunksWithEmptySlots_Ptr_8 = 40;
        public const int _0048_UnsafeChunkPtrList_ChunksWithEmptySlots_Length_4 = 48;
        public const int _0052_UnsafeChunkPtrList_ChunksWithEmptySlots_Capacity_4 = 52;
        public const int _0056_UnsafeChunkPtrList_ChunksWithEmptySlots_Allocator_4 = 56;
        public const int _0064_UnsafeUintList_hashes_Ptr_8 = 64;
        public const int _0072_UnsafeUintList_hashes_Length_4 = 72;
        public const int _0076_UnsafeUintList_hashes_Capacity_4 = 76;
        public const int _0080_UnsafeUintList_hashes_Allocator_4 = 80;
        public const int _0088_UnsafeChunkPtrList_chunks_Ptr_8 = 88;
        public const int _0096_UnsafeChunkPtrList_chunks_Length_4 = 96;
        public const int _0100_UnsafeChunkPtrList_chunks_Capacity_4 = 100;
        public const int _0104_UnsafeChunkPtrList_chunks_Allocator_4 = 104;
        public const int _0112_ChunkListMap_FreeChunksBySharedComponents_emptyNodes_4 = 112;
        public const int _0116_ChunkListMap_FreeChunksBySharedComponents_skipNodes_4 = 116;
        public const int _0120_ComponentTypeInArchetypePtr_Types_8 = 120;
        public const int _0128_Int32_EntityCount_4 = 128;
        public const int _0132_Int32_ChunkCapacity_4 = 132;
        public const int _0136_Int32_TypesCount_4 = 136;
        public const int _0140_Int32_InstanceSize_4 = 140;
        public const int _0144_Int32_InstanceSizeWithOverhead_4 = 144;
        public const int _0148_Int32_ManagedEntityPatchCount_4 = 148;
        public const int _0152_Int32_ScalarEntityPatchCount_4 = 152;
        public const int _0156_Int32_BufferEntityPatchCount_4 = 156;
        public const int _0160_Int32Ptr_Offsets_8 = 160;
        public const int _0168_Int32Ptr_SizeOfs_8 = 168;
        public const int _0176_Int32Ptr_BufferCapacities_8 = 176;
        public const int _0184_Int32Ptr_TypeMemoryOrder_8 = 184;
    }
}

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


    //public struct MyComponent : IComponentData
    //{
    //    public Entity Reference;
    //    public int Value;
    //}

    //public struct MyElement : IBufferElementData
    //{
    //    public Entity Reference;
    //    public int Value;
    //}

    //public class MySystem : SystemBase
    //{
    //    protected override void OnUpdate()
    //    {
    //        var ecb = new EntityCommandBuffer(Allocator.Temp);
    //        var entity = ecb.CreateEntity();
    //        ecb.AddComponent(entity, new MyComponent
    //        {
    //            Reference = entity,
    //            Value = 42,
    //        });
    //        var buffer = ecb.AddBuffer<MyElement>(entity);
    //        buffer.Add(new MyElement
    //        {
    //            Reference = entity,
    //            Value = 41
    //        });
    //        ecb.Playback(EntityManager);

    //        var createdEntity = GetSingletonEntity<MyComponent>();
    //        var createdComponent = GetSingleton<MyComponent>();

    //        Assert.AreEqual(createdComponent.Reference, createdEntity);
    //        Assert.AreNotEqual(createdEntity, entity);
    //        Assert.AreEqual(createdComponent.Value, 42);

    //        var createdBuffer = EntityManager.GetBuffer<MyElement>(createdEntity);
            
    //        Assert.AreEqual(createdBuffer[0].Reference, createdEntity);
    //        Assert.AreEqual(createdBuffer[0].Value, 41);
    //    }
    //}

    //[Test]
    //unsafe public void ECBReferenceTest()
    //{
    //    World.GetOrCreateSystem<MySystem>().Update();
    //}


    //public class TestSystem : SystemBase
    //{
    //    protected override void OnUpdate()
    //    {
    //        var ecb = new EntityCommandBuffer();
    //        var entity = ecb.CreateEntity();

    //    }
    //}

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

    public struct C1 : IComponentData { }
    public struct C2 : IComponentData { }
    public struct C3 : IComponentData { }

    [Test]
    unsafe public void HasTest()
    {


        Manager.CreateEntity(new[]
        {
            ComponentType.ReadWrite<C1>(),
            ComponentType.ReadWrite<C2>(),
            ComponentType.ReadWrite<C3>(),
        });

        Manager.CreateEntity(new[]
{
            ComponentType.ReadWrite<C1>(),
            ComponentType.ReadWrite<C2>(),
        });

        Manager.CreateEntity(new[]
        {
            ComponentType.ReadWrite<C1>(),
        });

        var anyC1 = Manager.CreateEntityQuery(ComponentType.ReadWrite<C1>());
        var chunksWithC1 = anyC1.CreateArchetypeChunkArray(Allocator.TempJob);

        var sum = 0;
        foreach (var chunk in chunksWithC1)
        {
            if(chunk.Has(ComponentType.ReadWrite<C1>(), ComponentType.ReadWrite<C2>(), ComponentType.ReadWrite<C3>()))
            {
                sum++;
            }
        }

        Assert.IsTrue(chunksWithC1.Length == 3);
        Assert.IsTrue(sum == 1);

        chunksWithC1.Dispose();
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
        AssertTypeSizesAreEqual<UnsafeExtensions.EntityArchetypeProxy>(unityType);
        AssertInstanceBytesAreEqual<UnsafeExtensions.EntityArchetypeProxy>(unityType);

        var flags = FieldComparisonFlags.AllowVoidPointerReplacement | FieldComparisonFlags.IgnorePointers;
        AssertFieldsAreEqual<UnsafeExtensions.EntityArchetypeProxy>(unityType, flags);
    }

    [Test, TestCategory(TestCategory.Integrity)]
    unsafe public void ArchetypeChunkProxy()
    {
        var unityType = typeof(ArchetypeChunk);
        AssertTypeSizesAreEqual<UnsafeExtensions.ArchetypeChunkProxy>(unityType);
        AssertInstanceBytesAreEqual<UnsafeExtensions.ArchetypeChunkProxy>(unityType);

        var flags = FieldComparisonFlags.AllowVoidPointerReplacement;
        AssertFieldsAreEqual<UnsafeExtensions.ArchetypeChunkProxy>(unityType, flags);
    }
}
