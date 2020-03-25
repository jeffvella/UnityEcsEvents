using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;
using System.Collections;
using System.Diagnostics;
using static Vella.Events.UnsafeExtensions;
using System.Collections.Generic;
using System.Linq;

namespace Vella.Events
{
    [DebuggerDisplay("Count = {Length}")]
    public unsafe struct ReadOnlyChunkCollection : IReadOnlyList<ArchetypeChunk>
    {
        private ArchetypeProxy* _archetype;
        private void* _componentStore;

        public int Length => _archetype->Chunks.Count;

        public ArchetypeChunk this[int index] => GetArchetypeChunk(index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeChunk First() => GetArchetypeChunk(0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeChunk Last() => GetArchetypeChunk(_archetype->Chunks.Count-1);

        public ReadOnlyChunkCollection(EntityArchetype archetype)
        {
            var entityArchetype = ((EntityArchetypeProxy*)&archetype);
            _archetype = entityArchetype->Archetype;
            _componentStore = entityArchetype->_DebugComponentStore;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ArchetypeChunk GetArchetypeChunk(int index)
        {
            ArchetypeChunkProxy chunk;
            chunk.m_Chunk = _archetype->Chunks.p[index];
            chunk.entityComponentStore = _componentStore;
            return UnsafeUtilityEx.AsRef<ArchetypeChunk>(&chunk);
        }

        public unsafe void CopyTo(void* destinationPtr)
        {
            UnsafeUtility.MemCpyStride(destinationPtr, sizeof(ArchetypeChunk), _archetype->Chunks.p, sizeof(void*), 
                sizeof(ArchetypeChunk), _archetype->Chunks.Count);
        }

        public unsafe void AddTo(ref UnsafeList<ArchetypeChunk> destination)
        {
            var dstLength = destination.Length;
            var srcLength = _archetype->Chunks.Count;
            destination.Resize(dstLength + srcLength);
            var start = (byte*)destination.Ptr + sizeof(ArchetypeChunk) * dstLength;
            UnsafeUtility.MemCpyStride(start, sizeof(EntityArchetype), _archetype->Chunks.p, sizeof(void*), sizeof(ArchetypeChunk), srcLength);
        }

        public unsafe void CopyTo(void* destinationPtr, int destinationOffsetElements)
        {
            UnsafeUtility.MemCpyStride((byte*)destinationPtr + sizeof(EntityArchetype) * destinationOffsetElements, 
                sizeof(EntityArchetype), _archetype->Chunks.p, sizeof(void*), sizeof(ArchetypeChunk), _archetype->Chunks.Count);
        }

        public struct Iterator
        {
            private ReadOnlyChunkCollection _source;
            private int _index;

            public Iterator(ref ReadOnlyChunkCollection buffer)
            {
                _source = buffer;
                _index = -1;
            }

            public bool MoveNext() => ++_index < _source.Length;

            public void Reset() => _index = -1;

            public ArchetypeChunk Current => _source[_index];
        }

        public List<ArchetypeChunk> ManagedDebugItems => ManagedEnumerable().ToList();

        public Iterator GetEnumerator() => new Iterator(ref this);

        int IReadOnlyCollection<ArchetypeChunk>.Count => _archetype->Chunks.Count;

        IEnumerator IEnumerable.GetEnumerator() => ManagedEnumerable().GetEnumerator();

        IEnumerator<ArchetypeChunk> IEnumerable<ArchetypeChunk>.GetEnumerator() => ManagedEnumerable().GetEnumerator();

        IEnumerable<ArchetypeChunk> ManagedEnumerable()
        {
            var enu = GetEnumerator();
            while (enu.MoveNext())
                yield return enu.Current;
        }
    }

}