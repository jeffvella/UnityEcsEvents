using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Vella.Events.Extensions;
using System.Collections.Generic;

namespace Vella.Events
{

    [DebuggerDisplay("Chunks={Length}")]
    public unsafe struct ArchetypeView
    {
        private ArchetypeProxy* _archetype;
        private void* _componentStore;

        public int Length => _archetype->Chunks.Count;

        public int ChunkCapacity => _archetype->Chunks.Capacity;

        public ArchetypeView(EntityArchetype archetype)
        {
            var entityArchetype = ((EntityArchetypeProxy*)&archetype);
            _archetype = entityArchetype->Archetype;
            _componentStore = entityArchetype->_DebugComponentStore;
        }

        public ArchetypeChunk this[int index] => GetArchetypeChunk(index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeChunk First() => GetArchetypeChunk(0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeChunk Last() => GetArchetypeChunk(_archetype->Chunks.Count - 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ArchetypeChunk GetArchetypeChunk(int index)
        {
            ArchetypeChunkProxy chunk;
            chunk.m_Chunk = _archetype->Chunks.p[index];
            chunk.entityComponentStore = _componentStore;
            return UnsafeUtilityEx.AsRef<ArchetypeChunk>(&chunk);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ArchetypeChunk* GetArchetypeChunkPtr(int index)
        {
            return (ArchetypeChunk*)((byte*)_archetype->Chunks.p + sizeof(void*) * index);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetChunkPtr(int index)
        {
            return *(byte**)((byte*)_archetype->Chunks.p + sizeof(void*) * index);
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

        public SimpleChunkIterator GetEnumerator() => new SimpleChunkIterator(ref this);

        public FilteringChunkIterator GetEnumerator(ChunkFilter filter, IteratorDirection direction = IteratorDirection.Forwards)
        {
            FilteringChunkIterator it;
            it._chunks = &_archetype->Chunks;
            it._filter = filter;
            it._step = (int)direction;

            if (direction == IteratorDirection.Forwards)
            {
                it._startIndex = -1;
                it._endIndex = _archetype->Chunks.Count-1;
            }
            else
            {
                it._startIndex = _archetype->Chunks.Count;
                it._endIndex = 0;
            }
            it._index = it._startIndex;
            return it;
        }

        public struct SimpleChunkIterator
        {
            private ArchetypeView _source;
            private int _index;

            public SimpleChunkIterator(ref ArchetypeView buffer)
            {
                _source = buffer;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext() => ++_index < _source.Length;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset() => _index = -1;

            public ArchetypeChunk Current => _source.GetArchetypeChunk(_index);
        }

        public ref struct FilteringChunkIterator
        {
            internal ArchetypeChunkDataProxy* _chunks;
            internal ChunkFilter _filter;
            internal int _index;
            internal int _step;
            internal int _startIndex;
            internal int _endIndex;

            public bool MoveNext()
            {
                while (_index != _endIndex)
                {
                    _index += _step;
                    switch (_filter)
                    {
                        case ChunkFilter.Full:
                            if (Current.Full)
                                return true;
                            break;
                        case ChunkFilter.Partial:
                            if (!Current.Full)
                                return true;
                            break;
                        default:
                            return true;
                    }
                }
                return false;
            }

            public void Reset() => _index = _startIndex;

            public FilteringChunkIterator GetEnumerator() => this;

            public ref ArchetypeChunk Current => ref *(ArchetypeChunk*)((byte*)_chunks->p + _index * sizeof(void*));
        }

        public List<ArchetypeChunk> ToList(ChunkFilter filter = ChunkFilter.None)
        {
            var result = new List<ArchetypeChunk>();
            var enu = GetEnumerator(filter);
            while (enu.MoveNext())
                result.Add(enu.Current);
            return result;
        }

        public List<ArchetypeChunk> Chunks => ToList();
        public List<ArchetypeChunk> FullChunks => ToList(ChunkFilter.Full);
        public List<ArchetypeChunk> PartialChunks => ToList(ChunkFilter.Partial);

    }

    public enum IteratorDirection
    {
        Forwards = 1,
        Backwards = -1,
    }

    public enum ChunkFilter
    {
        None = 0,
        Full,
        Partial,
    }

}