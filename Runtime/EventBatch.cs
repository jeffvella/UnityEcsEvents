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
    [DebuggerTypeProxy(typeof(EventBatchDebugView))]
    internal unsafe struct EventBatch
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
        public int StartingPoolSize;

        public bool HasComponent;
        public bool HasBuffer;

        public ComponentType BufferType;
        public int BufferTypeIndex;
        public int BufferSizeInChunk;
        public int BufferElementSize;
        public int BufferAlignmentInBytes;
        public int BufferLinkTypeIndex;

        // Destroying entities is a very costly operation and our event entities
        // must only live for 1 frame. So the approach here is to have two 
        // identical Archetypes for every event type, then Add/Remove the 'Disabled'
        // component. By default queries will ignore entities with a disabled component.
        // So they won't trigger reactive systems that are waiting for events to appear.

        public EntityArchetype Archetype;
        public EntityArchetype InactiveArchetype;

        // ArchetypeChunk methods such as GetNativeArray() need to scan the chunk types
        // because they may change from chunk-to-chunk when used in a job.
        // In an EventBatch the every chunk is the same and has Archetypes that 
        // should not change so the offsets can be cached.
        // Note these differ from Archetype/TypeInfo offsets, which exclude ChunkHeader.

        public ArchetypeOffsetsFromChunk Offsets;

        // Mostly for debug purposes, ArchetypeView sits on top of an Archetype 
        // and its members show the actual current counts/chunks.

        public ArchetypeView ActiveChunks;
        public ArchetypeView InactiveChunks;

        // Need cached arrays of ArchetypeChunk (for subset counts and StructuralChanges method arguments) 
        // internally unity has only 'Chunk' ptrs. So its either build ArchetypeChunks in advance or wrap 
        // Chunks on the fly when reading from ArchetypeView.
        
        public UnsafeList ActiveArchetypeChunks;
        public UnsafeList ActiveFullArchetypeChunks;
        public UnsafeList ActivePartialArchetypeChunk;
        public UnsafeList InactiveFullArchetypeChunks;
        public UnsafeList InactivePartialArchetypeChunk;

        public int EntityCount;
        public bool HasChanged;

        public static EventBatch CreateComponentBatch<T>(EntityManager em, ComponentType metaComponent, int startingPoolSize = 0, Allocator allocator = Allocator.Persistent) 
            where T : struct, IComponentData
        {
            return new EventBatch(em,new EventDefinition
            {
                MetaType = metaComponent, 
                StartingPoolSize = startingPoolSize,
                ComponentTypeInfo = TypeManager.GetTypeInfo<T>()

            }, allocator);
        }

        public static EventBatch CreateComponentAndBufferBatch<T1, T2>(EntityManager em, ComponentType metaComponent, int startingPoolSize = 0, Allocator allocator = Allocator.Persistent)
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

        public static EventBatch CreateBufferBatch<T>(EntityManager em, ComponentType metaComponent, int startingPoolSize = 0, Allocator allocator = Allocator.Persistent)
            where T : struct, IBufferElementData
        {
            return new EventBatch(em, new EventDefinition
            {
                MetaType = metaComponent,
                StartingPoolSize = startingPoolSize,
                BufferTypeInfo = TypeManager.GetTypeInfo<T>(),

            }, allocator);
        }

        public EventBatch(EntityManager em, EventDefinition definition, Allocator allocator = Allocator.Persistent) : this()
        {
            var componentType = ComponentType.FromTypeIndex(definition.ComponentTypeInfo.TypeIndex);
            var bufferType = ComponentType.FromTypeIndex(definition.BufferTypeInfo.TypeIndex);
            var bufferLinkType = ComponentType.ReadWrite<BufferLink>();

            HasBuffer = definition.BufferTypeInfo.TypeIndex != 0;
            HasComponent = definition.ComponentTypeInfo.TypeIndex != 0;

            var components = new List<ComponentType>()
            {
                definition.MetaType,
            };

            if (HasComponent)
            {
                components.AddRange(new[] {
                    componentType,
                });
            }

            if (HasBuffer)
            {
                components.AddRange(new[] {
                    bufferType,
                    bufferLinkType,
                });
            }

            Archetype = em.CreateArchetype(components.ToArray());

            var inactiveComponents = new List<ComponentType>(components)
            {
                ComponentType.ReadWrite<Disabled>()
            };

            InactiveArchetype = em.CreateArchetype(inactiveComponents.ToArray());


            Allocator = allocator;
            ComponentType = componentType;
            ComponentTypeIndex = definition.ComponentTypeInfo.TypeIndex;
            ComponentTypeSize = definition.ComponentTypeInfo.SizeInChunk;

            BufferType = bufferType;
            BufferTypeIndex = definition.BufferTypeInfo.TypeIndex;
            BufferElementSize = definition.BufferTypeInfo.ElementSize;
            BufferSizeInChunk = definition.BufferTypeInfo.SizeInChunk;
            BufferAlignmentInBytes = definition.BufferTypeInfo.AlignmentInBytes;
            BufferLinkTypeIndex = TypeManager.GetTypeIndex<BufferLink>();

            StartingPoolSize = definition.StartingPoolSize;

            ComponentQueue = new EventQueue(ComponentTypeIndex, ComponentTypeSize, BufferTypeIndex, BufferElementSize, allocator);
            Offsets = GetChunkOffsets(em, Archetype, definition.MetaType, componentType, bufferType, bufferLinkType);

            ActiveChunks = new ArchetypeView(Archetype);
            InactiveChunks = new ArchetypeView(InactiveArchetype);

            ActiveArchetypeChunks = new UnsafeList(Allocator.Persistent);
            ActiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
            ActivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);
            InactiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
            InactivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);

            if (definition.StartingPoolSize > 0)
            {
                CreateInactiveEntities(em, definition.StartingPoolSize);
                UpdateChunkCollections();
            }
        }

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
    }

    internal sealed class EventBatchDebugView
    {
        private EventBatch _entityEventSystem;

        public EventBatchDebugView(EventBatch input)
        {
            _entityEventSystem = input;
        }

        public bool HasBuffer => _entityEventSystem.HasBuffer;
        public bool HasComponent => _entityEventSystem.HasComponent;
    }

}