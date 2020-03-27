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
        public EntityArchetype InactiveArchetype;

        public Allocator Allocator;

        public int BufferLinkTypeIndex;
        private ArchetypeOffsetsFromChunk Offsets;

        //public UnsafeList<ArchetypeChunk> FullChunks;
        //public UnsafeList<ArchetypeChunk> PartialChunks;

        //private ArchetypeChunk ScratchChunk;
        //private int ScratchChunkIndex;

        //public UnsafeList<ArchetypeChunk> PartialChunks;


        //public int ActiveFullChunks;
        //public int ActiveEntityCount;
        //public int PartialChunkIndex;
        //internal int FullInactiveIndex;
        ////internal int RequiredChunks;
        //internal int FirstFullChunkIndex;

        public ArchetypeChunkView ActiveChunks;
        public ArchetypeChunkView InactiveChunks;

        public UnsafeList ActiveArchetypeChunks;
        public UnsafeList ActiveFullArchetypeChunks;
        public UnsafeList ActivePartialArchetypeChunk;
        public UnsafeList InactiveFullArchetypeChunks;
        public UnsafeList InactivePartialArchetypeChunk;

        internal bool RequiresActiveUpdate;
        internal bool RequiresInactiveUpdate;
        internal int EntityCount;
        private int StartingPoolSize;
        const int cachedChunkCount = 5;

        public bool IsInitialized { get; private set; }

        public static EventArchetype Create<T>(EntityManager em, ComponentType metaComponent, int startingPoolSize, Allocator allocator) where T : struct, IComponentData
        {
            return Create(em, metaComponent, startingPoolSize, TypeManager.GetTypeInfo<T>(), allocator);
        }

        //public void Initialize()
        //{
        //    if(StartingPoolSize > 0)
        //    {
        //        UpdateChunkCollections();
        //    }
        //    IsInitialized = true;
        //}

        public static EventArchetype Create(EntityManager em, ComponentType metaComponent, int startingPoolSize, TypeManager.TypeInfo componentTypeInfo, Allocator allocator)
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
                StartingPoolSize = startingPoolSize,

                Offsets = GetChunkOffsets(em, archetype, metaComponent, componentType)
            };

            batch.ActiveChunks = new ArchetypeChunkView(batch.Archetype);
            batch.InactiveChunks = new ArchetypeChunkView(batch.InactiveArchetype);
            //batch.FullChunks = new UnsafeList<ArchetypeChunk>(cachedChunkCount, Allocator.Persistent);
            //batch.PartialChunks = new UnsafeList<ArchetypeChunk>(cachedChunkCount, Allocator.Persistent);
            //batch.CreateChunksFullOfDisabledEntities(em, cachedChunkCount);

            batch.ActiveArchetypeChunks = new UnsafeList(Allocator.Persistent);
            batch.ActiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
            batch.ActivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);
            batch.InactiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
            batch.InactivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);

            if(startingPoolSize > 0)
            {
                batch.CreateInactiveEntities(em, startingPoolSize);
                batch.RequiresActiveUpdate = true;//not needed
                batch.RequiresInactiveUpdate = true;
                batch.UpdateChunkCollections();
            }
            batch.IsInitialized = true;
            return batch;
        }

        internal static EventArchetype Create<T1, T2>(EntityManager em, ComponentType metaComponent, int startingPoolSize, Allocator allocator) 
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

                StartingPoolSize = startingPoolSize,
                Archetype = archetype,
                InactiveArchetype = inactiveArchetype,

                Offsets = GetChunkOffsets(em, archetype, metaComponent, componentType, bufferType, bufferLinkType)
            };

            batch.ActiveChunks = new ArchetypeChunkView(batch.Archetype);
            batch.InactiveChunks = new ArchetypeChunkView(batch.InactiveArchetype);


            batch.ActiveArchetypeChunks = new UnsafeList(Allocator.Persistent);
            batch.ActiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
            batch.ActivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);
            batch.InactiveFullArchetypeChunks = new UnsafeList(Allocator.Persistent);
            batch.InactivePartialArchetypeChunk = new UnsafeList(Allocator.Persistent);

            if (startingPoolSize > 0)
            {
                batch.CreateInactiveEntities(em, startingPoolSize);
                batch.RequiresActiveUpdate = true;
                batch.RequiresInactiveUpdate = true;
                batch.UpdateChunkCollections();
            }
            batch.IsInitialized = true;
            return batch;
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
            //if (Entities == 0) // still needs an op to convert them, but we dont need to scan because we know already
            //{
            //    ActiveFull.Clear();
            //    ActivePartial.Clear();
            //    return;
            //}

            // if there are no active events for this archetype just nuke the whole batch and clear the active collections.

            if (RequiresActiveUpdate)
            {
                ActiveArchetypeChunks.Clear();
                ActiveFullArchetypeChunks.Clear();
                ActivePartialArchetypeChunk.Clear();

                ArchetypeChunk* archetypeChunkPtr;

                for (int x = 0; x < ActiveChunks.Length; x++)
                {
                    archetypeChunkPtr = ActiveChunks.GetArchetypeChunkPtr(x);
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
                RequiresActiveUpdate = false;
            }

            if (RequiresInactiveUpdate)
            {
                InactiveFullArchetypeChunks.Clear();
                InactivePartialArchetypeChunk.Clear();

                ArchetypeChunk* archetypeChunkPtr;

                for (int x = 0; x < InactiveChunks.Length; x++)
                {
                    archetypeChunkPtr = InactiveChunks.GetArchetypeChunkPtr(x);
                    if (archetypeChunkPtr->Full)
                    {
                        InactiveFullArchetypeChunks.Add(*archetypeChunkPtr);
                    }
                    else
                    {
                        InactivePartialArchetypeChunk.Add(*archetypeChunkPtr);
                    }
                }
                RequiresInactiveUpdate = false;
            }

            //batch.ActiveChunks.AddTo(ref data.Chunks);
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
            //FullChunks.Dispose();
            //PartialChunks.Dispose();

            ActiveArchetypeChunks.Dispose();
            InactiveFullArchetypeChunks.Dispose();
            ActiveFullArchetypeChunks.Dispose();
            InactivePartialArchetypeChunk.Dispose();
            ActivePartialArchetypeChunk.Dispose();
        }


    }

}