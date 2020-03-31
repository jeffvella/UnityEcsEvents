using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using System;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Collections.Generic;

namespace Vella.Events
{

    /// <summary>
    /// Contains all type and scheduling information for a specific event.
    /// </summary>
    [DebuggerDisplay("{ComponentType} Count={EntityCount}")]
    public unsafe struct EventBatch
    {
        public struct EventDefinition
        {
            public ComponentType MetaType;
            public TypeManager.TypeInfo ComponentTypeInfo;
            public TypeManager.TypeInfo BufferTypeInfo;
            public int StartingPoolSize;
        }

        public EventQueue ComponentQueue;
        public Allocator Allocator;

        public ComponentType ComponentType;
        public int ComponentTypeIndex;
        public int ComponentTypeSize;

        public bool HasBuffer;
        public ComponentType BufferType;
        public int BufferTypeIndex;
        public int BufferSizeInChunk;
        public int BufferElementSize;
        public int BufferAlignmentInBytes;
        public int BufferLinkTypeIndex;

        public EntityArchetype Archetype;
        public EntityArchetype InactiveArchetype;
        public ArchetypeOffsetsFromChunk Offsets;

        public ArchetypeChunkView ActiveChunks;
        public ArchetypeChunkView InactiveChunks;

        public UnsafeList ActiveArchetypeChunks;
        public UnsafeList ActiveFullArchetypeChunks;
        public UnsafeList ActivePartialArchetypeChunk;
        public UnsafeList InactiveFullArchetypeChunks;
        public UnsafeList InactivePartialArchetypeChunk;

        public int EntityCount;
        public int StartingPoolSize;
        public bool HasChanged;

        public static EventBatch Create<T>(EntityManager em, ComponentType metaComponent, int startingPoolSize = 0, Allocator allocator = Allocator.Persistent) 
            where T : struct, IComponentData
        {
            return new EventBatch(em,new EventDefinition
            {
                MetaType = metaComponent, 
                StartingPoolSize = startingPoolSize,
                ComponentTypeInfo = TypeManager.GetTypeInfo<T>()

            }, allocator);
        }

        internal static EventBatch Create<T1, T2>(EntityManager em, ComponentType metaComponent, int startingPoolSize = 0, Allocator allocator = Allocator.Persistent)
            where T1 : struct, IComponentData
            where T2 : struct, IBufferElementData
        {
            return new EventBatch(em, new EventDefinition
            {
                MetaType = metaComponent,
                StartingPoolSize = startingPoolSize,
                ComponentTypeInfo = TypeManager.GetTypeInfo<T1>(),
                BufferTypeInfo = TypeManager.GetTypeInfo<T2>(),

            }, allocator);
        }

        public EventBatch(EntityManager em, EventDefinition definition, Allocator allocator = Allocator.Persistent) : this()
        {
            var componentType = ComponentType.FromTypeIndex(definition.ComponentTypeInfo.TypeIndex);
            var bufferType = ComponentType.FromTypeIndex(definition.BufferTypeInfo.TypeIndex);
            var bufferLinkType = ComponentType.ReadWrite<BufferLink>();

            HasBuffer = definition.BufferTypeInfo.TypeIndex != 0;

            var activeComponents = new List<ComponentType>()
            {
                definition.MetaType,
                componentType,
            };

            if (HasBuffer)
            {
                activeComponents.AddRange(new[] {
                    bufferType,
                    bufferLinkType,
                });
            }

            var inactiveComponents = new List<ComponentType>(activeComponents)
            {
                ComponentType.ReadWrite<Disabled>()
            };

            Allocator = allocator;
            HasBuffer = false;

            ComponentType = componentType;
            ComponentTypeIndex = definition.ComponentTypeInfo.TypeIndex;
            ComponentTypeSize = definition.ComponentTypeInfo.SizeInChunk;
            ComponentQueue = new EventQueue(definition.ComponentTypeInfo.SizeInChunk, allocator);

            BufferType = bufferType;
            BufferTypeIndex = definition.BufferTypeInfo.TypeIndex;
            BufferElementSize = definition.BufferTypeInfo.ElementSize;
            BufferSizeInChunk = definition.BufferTypeInfo.SizeInChunk;
            BufferAlignmentInBytes = definition.BufferTypeInfo.AlignmentInBytes;
            BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>();

            Archetype = em.CreateArchetype(activeComponents.ToArray());
            InactiveArchetype = em.CreateArchetype(inactiveComponents.ToArray());
            StartingPoolSize = definition.StartingPoolSize;

            Offsets = GetChunkOffsets(em, Archetype, definition.MetaType, componentType, bufferType, bufferLinkType);

            ActiveChunks = new ArchetypeChunkView(Archetype);
            InactiveChunks = new ArchetypeChunkView(InactiveArchetype);
            ActiveArchetypeChunks = new UnsafeList(Allocator.Persistent);
            ActiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
            ActivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);
            InactiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
            InactivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);

            if(definition.StartingPoolSize > 0)
            {
                CreateInactiveEntities(em, definition.StartingPoolSize);
                UpdateChunkCollections();
            }

            //batch.Dedicated = new DedicatedChunk(em, ComponentType);
        }

        public struct DedicatedChunk
        {
            private UnsafeEntityManager _uem;
            private Entity Entity;
            private ArchetypeChunk Chunk;
            private bool IsActive;
            private byte* ComponentOffset;
            private EntityArchetype _dedicatedActiveArchetype;
            private void* _dedicatedActiveArchetypePtr;
            private void* _dedicatedInactiveArchetypePtr;
            private EntityArchetype _dedicatedInactiveArchetype;

            public DedicatedChunk(EntityManager em, ComponentType componentType) : this()
            {
                //_uem = new UnsafeEntityManager(em);

                //_dedicatedActiveArchetype = em.CreateArchetype(new[]
                //{
                //    ComponentType.ReadWrite<EntityEvent>(),
                //    componentType,
                //    ComponentType.ReadWrite<DedicatedFlag>(),
                //});

                //_dedicatedActiveArchetypePtr = _dedicatedActiveArchetype.GetArchetypePtr();
                //_dedicatedInactiveArchetype = em.CreateArchetype(new[]
                //{
                //    ComponentType.ReadWrite<EntityEvent>(),
                //    componentType,
                //    ComponentType.ReadWrite<DedicatedFlag>(),
                //    ComponentType.ReadWrite<Disabled>(),
                //});

                //_dedicatedInactiveArchetypePtr = _dedicatedInactiveArchetype.GetArchetypePtr();

                //Entity = em.CreateEntity(_dedicatedInactiveArchetype);
                //Chunk = em.GetChunk(Entity);
          
                //IsActive = false;
                //ComponentOffset = GetComponentOffsetFromChunkPtr(em, _dedicatedInactiveArchetype, Chunk, componentType);
            }

            public void Activate()
            {

            }

            public void Deactivate()
            {

            }
        }

        public struct DedicatedFlag : IComponentData
        {

        }

        //internal static EventBatch Create<T1, T2>(EntityManager em, ComponentType metaComponent, int startingPoolSize, Allocator allocator) 
        //    where T1 : struct, IComponentData 
        //    where T2 : struct, IBufferElementData
        //{
        //    var componentTypeInfo = TypeManager.GetTypeInfo<T1>();
        //    var componentType = ComponentType.FromTypeIndex(componentTypeInfo.TypeIndex);

        //    var bufferTypeInfo = TypeManager.GetTypeInfo<T2>();
        //    var bufferType = ComponentType.FromTypeIndex(bufferTypeInfo.TypeIndex);
        //    var bufferLinkType = ComponentType.ReadWrite<BufferLink>();

        //    var archetype = em.CreateArchetype(new[]
        //    {
        //        metaComponent,
        //        componentType,
        //        bufferType,
        //        bufferLinkType,
        //    });

        //    var inactiveArchetype = em.CreateArchetype(new[]
        //    {
        //        metaComponent,
        //        componentType,
        //        bufferType,
        //        bufferLinkType,
        //        ComponentType.ReadWrite<Disabled>(),
        //    });

        //    var batch = new EventBatch
        //    {
        //        Allocator = allocator,
        //        HasBuffer = true,

        //        ComponentType = componentType,
        //        ComponentTypeIndex = componentTypeInfo.TypeIndex,
        //        ComponentTypeSize = UnsafeUtility.SizeOf<T1>(),

        //        BufferType = bufferType,
        //        BufferTypeIndex = bufferTypeInfo.TypeIndex,
        //        BufferElementSize = bufferTypeInfo.ElementSize,
        //        BufferSizeInChunk = bufferTypeInfo.SizeInChunk,
        //        BufferAlignmentInBytes = bufferTypeInfo.AlignmentInBytes,
        //        BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>(),

        //        ComponentQueue = new EventQueue(UnsafeUtility.SizeOf<T1>(), UnsafeUtility.SizeOf<T2>(), allocator),


        //        StartingPoolSize = startingPoolSize,
        //        Archetype = archetype,
        //        InactiveArchetype = inactiveArchetype,

        //        Offsets = GetChunkOffsets(em, archetype, metaComponent, componentType, bufferType, bufferLinkType)
        //    };

        //    batch.ActiveChunks = new ArchetypeChunkView(batch.Archetype);
        //    batch.InactiveChunks = new ArchetypeChunkView(batch.InactiveArchetype);
        //    batch.ActiveArchetypeChunks = new UnsafeList(Allocator.Persistent);
        //    batch.ActiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
        //    batch.ActivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);
        //    batch.InactiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
        //    batch.InactivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent); 

        //    if (startingPoolSize > 0)
        //    {
        //        batch.CreateInactiveEntities(em, startingPoolSize);
        //        batch.UpdateChunkCollections();
        //    }
        //    return batch;
        //}

        private void CreateInactiveEntities(EntityManager em, int entityCount)
        {
            var capacity = InactiveArchetype.ChunkCapacity;
            var chunkCount = entityCount / capacity + ((entityCount % capacity == 0) ? 0 : 1);
            var newChunks = new NativeArray<ArchetypeChunk>(chunkCount, Allocator.Temp);
            em.CreateChunk(InactiveArchetype, newChunks, entityCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetComponentPointer(ArchetypeChunk chunk) => *(byte**)&chunk + Offsets.ComponentOffset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetMetaPointer(ArchetypeChunk chunk) => *(byte**)&chunk + Offsets.MetaOffset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetBufferPointer(ArchetypeChunk chunk) => *(byte**)&chunk + Offsets.BufferOffset;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* GetBufferLinkPointer(ArchetypeChunk chunk) => *(byte**)&chunk + Offsets.BufferLinkOffset;

        private static ArchetypeOffsetsFromChunk GetChunkOffsets(EntityManager em, EntityArchetype archetype, ComponentType metaType, ComponentType componentType, ComponentType bufferType = default, ComponentType linkType = default)
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

        public void UpdateChunkCollections()
        {
            ActiveArchetypeChunks.Clear();
            ActiveFullArchetypeChunks.Clear();
            ActivePartialArchetypeChunk.Clear();
            InactiveFullArchetypeChunks.Clear();
            InactivePartialArchetypeChunk.Clear();

            for (int x = 0; x < ActiveChunks.Length; x++)
            {
                var archetypeChunkPtr = ActiveChunks.GetArchetypeChunkPtr(x);
                if (archetypeChunkPtr->Full)
                {
                    ActiveFullArchetypeChunks.Add(*archetypeChunkPtr);
                }
                else
                {
                    ActivePartialArchetypeChunk.Add(*archetypeChunkPtr);
                }
                ActiveArchetypeChunks.Add(*archetypeChunkPtr);
            }

            for (int x = 0; x < InactiveChunks.Length; x++)
            {
                var archetypeChunkPtr = InactiveChunks.GetArchetypeChunkPtr(x);
                if (archetypeChunkPtr->Full)
                {
                    InactiveFullArchetypeChunks.Add(*archetypeChunkPtr);
                }
                else
                {
                    InactivePartialArchetypeChunk.Add(*archetypeChunkPtr);
                }
            }
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
            ActiveArchetypeChunks.Dispose();
            InactiveFullArchetypeChunks.Dispose();
            ActiveFullArchetypeChunks.Dispose();
            InactivePartialArchetypeChunk.Dispose();
            ActivePartialArchetypeChunk.Dispose();
        }

        public void SetComponentData()
        {
            MultiAppendBuffer.Reader queuedComponents = ComponentQueue.GetComponentReader();
            var componentOffset = Offsets.ComponentOffset;
            for (int j = 0; j < Archetype.ChunkCount; j++)
            {
                ArchetypeChunk* archetypeChunkPtr = ActiveChunks.GetArchetypeChunkPtr(j);
                byte* componentsInChunkPtr = (byte*)archetypeChunkPtr->GetChunkPtr() + componentOffset;
                queuedComponents.CopyTo(componentsInChunkPtr, archetypeChunkPtr->Count * ComponentTypeSize);
            }

            if (HasBuffer)
            {
                MultiAppendBuffer.Reader links = ComponentQueue.GetLinksReader();

                for (int j = 0; j < Archetype.ChunkCount; j++)
                {
                    ArchetypeChunk chunk = ((ArchetypeChunk*)ActiveArchetypeChunks.Ptr)[j];
                    var entityCount = chunk.Count;

                    byte* chunkBufferHeaders = GetBufferPointer(chunk);
                    byte* chunkLinks = GetBufferLinkPointer(chunk);

                    links.CopyTo(chunkLinks, entityCount * UnsafeUtility.SizeOf<BufferLink>());

                    for (int x = 0; x < entityCount; x++)
                    {
                        BufferHeaderProxy* bufferHeader = (BufferHeaderProxy*)(chunkBufferHeaders + x * BufferSizeInChunk);
                        BufferLink* link = (BufferLink*)(chunkLinks + x * UnsafeUtility.SizeOf<BufferLink>());

                        ref var source = ref ComponentQueue._bufferData.GetBuffer(link->ThreadIndex);
                        BufferHeaderProxy.Assign(bufferHeader, source.Ptr + link->Offset, link->Length, BufferElementSize, BufferAlignmentInBytes, default, default);
                    }
                }
            }
        }
    }

}