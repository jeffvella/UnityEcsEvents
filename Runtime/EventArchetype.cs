using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using System;
using System.Runtime.CompilerServices;

namespace Vella.Events
{
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
                ComponentType.ReadWrite<BufferLink>(),
                //ComponentType.ReadWrite<EventDebugInfo>()
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

                BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>(),

                Archetype = archetype,
                
                Offsets = CreateChunkSchema(em, archetype, metaComponent, componentType)
            };

            return batch;
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
                //ComponentType.ReadWrite<EventDebugInfo>()
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

                Offsets = CreateChunkSchema(em, archetype, metaComponent, componentType, bufferType, bufferLinkType)
            };

            return batch;
        }

        internal void Dispose()
        {
            ComponentQueue.Dispose();
        }


    }

}