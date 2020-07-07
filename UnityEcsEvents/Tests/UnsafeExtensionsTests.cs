using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Vella.Events;
using Vella.Events.Extensions;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

namespace ExtensionsTests
{
    public struct MyTestData : IComponentData
    { 
        public int Value;
    }

    public class EntityArchetypeExtensionsTests : SimpleTestFixture
    {
        /*[Test]
        public unsafe void CopiesChunksFromEntityArchetype([Values(1, 1000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<MyTestData>());

            var entities = new NativeArray<Entity>(entityCount, Allocator.Persistent);

            m_Manager.CreateEntity(archetype, entities);

            var destination = new NativeArray<ArchetypeChunk>(archetype.ChunkCount, Allocator.Persistent);

            m_Manager.Unsafe.CopyChunks(archetype, destination);

            var chunksFromEntityManager = m_Manager.GetAllChunks();
            
            AssertBytesAreEqual(destination, chunksFromEntityManager);

            chunksFromEntityManager.Dispose();

            destination.Dispose();
            entities.Dispose();
        }*/

        public unsafe void AssertBytesAreEqual<T>(NativeArray<T> arr1, NativeArray<T> arr2) where T : struct
        {
            var ptr1 = arr1.GetUnsafePtr();
            var ptr2 = arr2.GetUnsafePtr();
            var size = arr1.Length * UnsafeUtility.SizeOf<T>();

            Assert.AreEqual(arr1.Length, arr2.Length);
            Assert.AreEqual(0, UnsafeUtility.MemCmp(ptr1, ptr2, size));
        }

    }

}
