using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Vella.Events;
using Vella.Events.Extensions;
using Vella.Tests.Attributes;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

class ProxyTests : ECSTestsFixture
{
    [Test, TestCategory(TestCategory.Integrity)]
    unsafe public void GetComponentPtr()
    {
        World.GetOrCreateSystem<GetComponentPtrSystem>().Update();
    }

    [DisableAutoCreation, AlwaysUpdateSystem]
    public unsafe class GetComponentPtrSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var entity = EntityManager.CreateEntity(ComponentType.ReadWrite<EcsTestData>());
            var uem = new UnsafeEntityManager(EntityManager);
            var inputTypeIndex = TypeManager.GetTypeIndex<EcsTestData>();
            var missingComponentTypeIndex = TypeManager.GetTypeIndex<EcsTestData2>();

            byte* result1 = null;
            byte* result2 = null;
            byte* actualPtr = null;

            Entities.ForEach((Entity e, ref EcsTestData data) =>
            {
                result1 = (byte*)uem.GetComponentPtr<EcsTestData>(e);
                result2 = uem.GetComponentPtr(e, inputTypeIndex);
                actualPtr = (byte*)UnsafeUtility.AddressOf(ref data);

            }).Run();

            if (result1 == null)
                Assert.Fail();
            if (result2 == null)
                Assert.Fail();
            if (actualPtr == null)
                Assert.Fail();

            Assert.AreEqual(*(long*)result1, *(long*)result2);
            Assert.AreEqual(*(long*)actualPtr, *(long*)result2);

        }
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
        AssertTypeSizesAreEqual<EntityArchetypeProxy>(unityType);
        AssertInstanceBytesAreEqual<EntityArchetypeProxy>(unityType);

        var flags = FieldComparisonFlags.AllowVoidPointerReplacement | FieldComparisonFlags.IgnorePointers;
        AssertFieldsAreEqual<EntityArchetypeProxy>(unityType, flags);
    }

    [Test, TestCategory(TestCategory.Integrity)]
    unsafe public void ArchetypeChunkProxy()
    {
        var unityType = typeof(ArchetypeChunk);
        AssertTypeSizesAreEqual<ArchetypeChunkProxy>(unityType);
        AssertInstanceBytesAreEqual<ArchetypeChunkProxy>(unityType);

        var flags = FieldComparisonFlags.AllowVoidPointerReplacement;
        AssertFieldsAreEqual<ArchetypeChunkProxy>(unityType, flags);
    }
}
