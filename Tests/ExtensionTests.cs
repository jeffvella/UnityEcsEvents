using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Vella.Events;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

namespace ExtensionsTests
{
    class EntityArchetypeExtensionsTests : ECSTestsFixture
    {
        [Test, Category("Compatability")]
        unsafe public void ProxyStructuresMatchEntitiesPackage()
        {
            Assert.IsTrue(sizeof(EntityArchetype) == sizeof(EntityArchetypeExtensions.EntityArchetypeProxy));
            Assert.IsTrue(sizeof(ArchetypeChunk) == sizeof(EntityArchetypeExtensions.ArchetypeChunkProxy));
        }

        [Test, Category("Functionality")]
        unsafe public void CopiesChunksFromEntityArchetype([Values(0, 10000)] int entityCount)
        {
            var archetype = m_Manager.CreateArchetype(typeof(EcsTestData));
            var entities = new NativeArray<Entity>(entityCount, Allocator.TempJob);
            m_Manager.CreateEntity(archetype, entities);

            var clone = new NativeArray<ArchetypeChunk>(archetype.ChunkCount, Allocator.Temp);
            archetype.CopyChunksTo(clone);

            var actual = new NativeList<ArchetypeChunk>(archetype.ChunkCount, Allocator.Temp);
            var allChunks = m_Manager.GetAllChunks();
            for (int i = 0; i < allChunks.Length; i++)
            {
                var chunk = allChunks[i];
                if (chunk.Archetype == archetype)
                    actual.Add(chunk);
            }

            AssertBytesAreEqual(clone, actual);

            clone.Dispose();
            actual.Dispose();
            allChunks.Dispose();
            entities.Dispose();
        }

    }

}
