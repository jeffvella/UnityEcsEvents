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
