using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using System;
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

            public ArchetypeChunk Current => _source.GetArchetypeChunk(_index);
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

    /// <summary>
    /// Contains all type and scheduling information for a specific event.
    /// </summary>
    public unsafe struct EventArchetype
    {
        public EventQueue ComponentQueue;
        public bool HasBuffer;

        public ComponentType ComponentType;
        public TypeManager.TypeInfo ComponentTypeInfo;
        public int ComponentTypeIndex;
        public int ComponentTypeSize;

        public ComponentType BufferType;
        public TypeManager.TypeInfo BufferTypeInfo;
        public int BufferTypeIndex;
        public int BufferTypeSize;

        public EntityArchetype Archetype;
        public Allocator Allocator;

        public int BufferLinkTypeIndex;
        private ArchetypeOffsetsFromChunk Offsets;

        public UnsafeList<ArchetypeChunk> FullChunks;
        public UnsafeList<ArchetypeChunk> PartialChunks;

        //private ArchetypeChunk ScratchChunk;
        //private int ScratchChunkIndex;

        //public UnsafeList<ArchetypeChunk> PartialChunks;
        public EntityArchetype InactiveArchetype;

        public int ActiveFullChunks;
        public int ActiveEntityCount;
        public int PartialChunkIndex;
        internal int FullInactiveIndex;
        //internal int RequiredChunks;
        internal int FirstFullChunkIndex;

        public ReadOnlyChunkCollection ActiveChunks;
        public ReadOnlyChunkCollection InactiveChunks;

        const int cachedChunkCount = 5;

        public static EventArchetype Create<T>(EntityManager em, ComponentType metaComponent, Allocator allocator) where T : struct, IComponentData
        {
            return Create(em, metaComponent, TypeManager.GetTypeInfo<T>(), allocator);
        }


        public static EventArchetype Create(EntityManager em, ComponentType metaComponent, TypeManager.TypeInfo componentTypeInfo, Allocator allocator)
        {
            var componentType = ComponentType.FromTypeIndex(componentTypeInfo.TypeIndex);

            var archetype = em.CreateArchetype(new[]
            {
                metaComponent,
                componentType,
            });

            var inactiveArchetype = em.CreateArchetype(new[]
            {
                metaComponent,
                componentType,
                ComponentType.ReadWrite<Disabled>(),
            });

            var batch = new EventArchetype
            {
                Allocator = allocator,
                HasBuffer = false,

                ComponentType = componentType,
                ComponentTypeInfo = componentTypeInfo,
                ComponentTypeIndex = componentTypeInfo.TypeIndex,
                ComponentTypeSize = componentTypeInfo.SizeInChunk,
                ComponentQueue = new EventQueue(componentTypeInfo.SizeInChunk, allocator),

                Archetype = archetype,
                InactiveArchetype = inactiveArchetype,

                Offsets = CreateChunkSchema(em, archetype, metaComponent, componentType)
            };

            batch.ActiveChunks = new ReadOnlyChunkCollection(batch.Archetype);
            batch.InactiveChunks = new ReadOnlyChunkCollection(batch.InactiveArchetype);
            batch.FullChunks = new UnsafeList<ArchetypeChunk>(cachedChunkCount, Allocator.Persistent);
            batch.PartialChunks = new UnsafeList<ArchetypeChunk>(cachedChunkCount, Allocator.Persistent);
            batch.CreateChunksFullOfDisabledEntities(em, cachedChunkCount);

            return batch;
        }

        
        internal static EventArchetype Create<T1, T2>(EntityManager em, ComponentType metaComponent, Allocator allocator) 
            where T1 : struct, IComponentData 
            where T2 : struct, IBufferElementData
        {
            var componentTypeInfo = TypeManager.GetTypeInfo<T1>();
            var componentType = ComponentType.FromTypeIndex(componentTypeInfo.TypeIndex);

            var bufferTypeInfo = TypeManager.GetTypeInfo<T2>();
            var bufferType = ComponentType.FromTypeIndex(bufferTypeInfo.TypeIndex);
            var bufferLinkType = ComponentType.ReadWrite<BufferLink>();

            var archetype = em.CreateArchetype(new[]
            {
                metaComponent,
                componentType,
                bufferType,
                bufferLinkType,
            });

            var inactiveArchetype = em.CreateArchetype(new[]
            {
                metaComponent,
                componentType,
                bufferType,
                bufferLinkType,
                ComponentType.ReadWrite<Disabled>(),
            });

            var batch = new EventArchetype
            {
                Allocator = allocator,
                HasBuffer = true,

                ComponentType = componentType,
                ComponentTypeInfo = componentTypeInfo,
                ComponentTypeIndex = componentTypeInfo.TypeIndex,
                ComponentTypeSize = UnsafeUtility.SizeOf<T1>(),

                BufferType = bufferType,
                BufferTypeIndex = bufferTypeInfo.TypeIndex,
                BufferTypeInfo = bufferTypeInfo,
                BufferTypeSize = UnsafeUtility.SizeOf<T2>(),

                ComponentQueue = new EventQueue(UnsafeUtility.SizeOf<T1>(), UnsafeUtility.SizeOf<T2>(), allocator),
                BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>(),

                Archetype = archetype,
                InactiveArchetype = inactiveArchetype,

                Offsets = CreateChunkSchema(em, archetype, metaComponent, componentType, bufferType, bufferLinkType)
            };

            batch.ActiveChunks = new ReadOnlyChunkCollection(batch.Archetype);
            batch.InactiveChunks = new ReadOnlyChunkCollection(batch.InactiveArchetype);
            batch.FullChunks = new UnsafeList<ArchetypeChunk>(cachedChunkCount, Allocator.Persistent);
            batch.PartialChunks = new UnsafeList<ArchetypeChunk>(cachedChunkCount, Allocator.Persistent);
            batch.CreateChunksFullOfDisabledEntities(em, cachedChunkCount);

            return batch;
        }

        private void CreateChunksFullOfDisabledEntities(EntityManager em, int chunkCount)
        {
            var newChunks = new NativeArray<ArchetypeChunk>(chunkCount, Allocator.Temp);
            em.CreateChunk(InactiveArchetype, newChunks, InactiveArchetype.ChunkCapacity * chunkCount);
            FullChunks.AddRange(new UnsafeList<ArchetypeChunk>((ArchetypeChunk*)newChunks.GetUnsafePtr(), newChunks.Length));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetComponentPointer(ArchetypeChunk chunk) => *(byte**)&chunk + Offsets.ComponentOffset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetMetaPointer(ArchetypeChunk chunk) => *(byte**)&chunk + Offsets.MetaOffset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetBufferPointer(ArchetypeChunk chunk) => *(byte**)&chunk + Offsets.BufferOffset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetBufferLinkPointer(ArchetypeChunk chunk) => *(byte**)&chunk + Offsets.BufferLinkOffset;


        private static ArchetypeOffsetsFromChunk CreateChunkSchema(EntityManager em, EntityArchetype archetype, ComponentType metaType, ComponentType componentType, ComponentType bufferType = default, ComponentType linkType = default)
        {
            ArchetypeOffsetsFromChunk schema = default;

            var tmpEntity = em.CreateEntity(archetype);
            var chunk = em.GetChunk(tmpEntity);
            byte* chunkPtr = *(byte**)&chunk;
            var tmp = em.GetArchetypeChunkComponentType<ChunkHeader>(false);
            var types = archetype.GetComponentTypes();

            UnsafeUtility.CopyStructureToPtr(ref metaType.TypeIndex, UnsafeUtility.AddressOf(ref tmp));
            schema.MetaOffset = (byte*)chunk.GetNativeArray(tmp).GetUnsafeReadOnlyPtr() - chunkPtr;

            UnsafeUtility.CopyStructureToPtr(ref componentType.TypeIndex, UnsafeUtility.AddressOf(ref tmp));
            schema.ComponentOffset = (byte*)chunk.GetNativeArray(tmp).GetUnsafeReadOnlyPtr() - chunkPtr;

            if (bufferType != default)
            {
                UnsafeUtility.CopyStructureToPtr(ref linkType.TypeIndex, UnsafeUtility.AddressOf(ref tmp));
                schema.BufferLinkOffset = (byte*)chunk.GetNativeArray(tmp).GetUnsafeReadOnlyPtr() - chunkPtr;

                UnsafeUtility.CopyStructureToPtr(ref bufferType.TypeIndex, UnsafeUtility.AddressOf(ref tmp));
                schema.BufferOffset = (byte*)chunk.GetNativeArray(tmp).GetUnsafeReadOnlyPtr() - chunkPtr;
            }

            em.DestroyEntity(tmpEntity);
            return schema;
        }

        public struct ArchetypeOffsetsFromChunk : IComponentData
        {
            public long MetaOffset;
            public long ComponentOffset;
            public long BufferLinkOffset;
            public long BufferOffset;
        }

        internal void Dispose()
        {
            ComponentQueue.Dispose();
        }


    }

}