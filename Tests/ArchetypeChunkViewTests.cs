using System;
using System.Diagnostics;
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
using Vella.Events.Extensions;
using Vella.Tests.Attributes;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

public class ArchetypeChunkViewTests : EntityPerformanceTestFixture
{
    [Test]
    public unsafe void Iterator()
    {
        var entityCount = 10000;
        var archetype = m_Manager.CreateArchetype(ComponentType.ReadWrite<EcsTestData>());


        var cap = archetype.ChunkCapacity;
        var fullChunksCount = entityCount / cap;
        var partialChunkCount = entityCount % cap == 0 ? 0 : 1;
        var totalChunks = fullChunksCount + partialChunkCount;
        var chunks = new NativeArray<ArchetypeChunk>(totalChunks, Allocator.Temp);

        m_Manager.CreateChunk(archetype, chunks, entityCount);
        Assert.AreEqual(archetype.ChunkCount, totalChunks);

        var view = new ArchetypeView(archetype);
        Assert.AreEqual(archetype.ChunkCount, view.Length);

        var fullChunksArr = new NativeArray<ArchetypeChunk>(chunks.Where(c => c.Full).ToArray(), Allocator.Temp);
        var partialChunksArr = new NativeArray<ArchetypeChunk>(chunks.Where(c => !c.Full).ToArray(), Allocator.Temp);

        Assert.AreEqual(fullChunksArr.Length, fullChunksCount);
        Assert.AreEqual(partialChunksArr.Length, partialChunkCount);

        AssertIteratorWorksWithFilter(ref chunks, view, ChunkFilter.None);
        AssertIteratorWorksWithFilter(ref fullChunksArr, view, ChunkFilter.Full);
        AssertIteratorWorksWithFilter(ref partialChunksArr, view, ChunkFilter.Partial);
    }

    private static unsafe void AssertIteratorWorksWithFilter(ref NativeArray<ArchetypeChunk> chunks, ArchetypeView view, ChunkFilter filter)
    {
        var i = 0;
        var enu = view.GetEnumerator(filter, IteratorDirection.Forwards);
        foreach (ref var chunk in enu)
        {
            chunk.Invalid();
            var match = chunk.GetChunkPtr() == chunks[i].GetChunkPtr();
            if (!match)
                Debugger.Break();

            Assert.IsTrue(match);
            i++;
        }
        Assert.AreEqual(i, chunks.Length);

        i = chunks.Length-1;
        enu = view.GetEnumerator(filter, IteratorDirection.Backwards);
        foreach (ref var chunk in enu)
        {
            var match = chunk.GetChunkPtr() == chunks[i].GetChunkPtr();
            if (!match)
                Debugger.Break();

            Assert.IsTrue(match);
            i--;
        }
        Assert.AreEqual(i,-1);
    }
}


