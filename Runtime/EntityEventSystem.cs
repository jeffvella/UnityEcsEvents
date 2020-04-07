using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Vella.Events.Extensions;
using System.Linq;

namespace Vella.Events
{

    [DisableAutoCreation]
    [DebuggerTypeProxy(typeof(EntityEventSystemDebugView))]
    public unsafe class EntityEventSystem : SystemBase
    {
        private void* _dataPtr;

        internal ref EventSystemData Data => ref UnsafeUtilityEx.AsRef<EventSystemData>(_dataPtr);

        internal struct EventSystemData
        {
            public UnsafeHashMap<int, int> TypeIndexToBatchMap;
            public UnsafeList<EventBatch> Batches;
            public UnsafeEntityManager UnsafeEntityManager;
            public StructuralChangeQueue StructuralChanges;
            public ComponentType EventComponent;
            public int DisabledTypeIndex;
            public int BatchCount;
            public int EntityCount;
            public bool HasChanged;
        }

        protected override void OnCreate()
        {
            EventSystemData data = default;
            data.TypeIndexToBatchMap = new UnsafeHashMap<int, int>(1, Allocator.Persistent);
            data.Batches = new UnsafeList<EventBatch>(1, Allocator.Persistent);
            data.UnsafeEntityManager = new UnsafeEntityManager(EntityManager);
            data.StructuralChanges = new StructuralChangeQueue(data.UnsafeEntityManager, Allocator.Persistent);
            data.EventComponent = ComponentType.ReadOnly<EntityEvent>();
            data.DisabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();

            _dataPtr = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<EventSystemData>(),
                UnsafeUtility.AlignOf<EventSystemData>(), Allocator.Persistent);
            UnsafeUtility.CopyStructureToPtr(ref data, _dataPtr);
        }

        protected override void OnUpdate()
        {
            if (Data.BatchCount == 0)
                return;

            ProcessQueuedEvents();

            if (Data.EntityCount > 0 || Data.HasChanged)
            {
                Data.StructuralChanges.Apply();
                UpdateChunkCollections();
                SetComponents();
                ClearQueues();
            }
        }

        internal void ProcessQueuedEvents()
        {
            var batchCount = Data.Batches.Length;
            var batchesPtr = (byte*)Data.Batches.Ptr;
            var dataPtr = (byte*)_dataPtr;

            Job.WithCode(() =>
            {
                ref var data = ref UnsafeUtilityEx.AsRef<EventSystemData>(dataPtr);

                data.StructuralChanges.Clear();
                data.EntityCount = 0;
                data.HasChanged = false;

                for (int i = 0; i < data.Batches.Length; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventBatch>(batchesPtr, i);

                    var requiredEntities = batch.ComponentQueue.Count();
                    data.EntityCount += requiredEntities;
                    batch.HasChanged = batch.EntityCount != requiredEntities;

                    if (!batch.HasChanged)
                    {
                        // Don't create or delete anything; 
                        // Just set component data on existing entities.
                        continue;
                    }

                    data.HasChanged = true;
                    batch.EntityCount = requiredEntities;

                    if (requiredEntities == 0)
                    {
                        // Deactivate all 
                        if (batch.ActiveChunks.Length != 0)
                        {
                            data.StructuralChanges.AddComponentToChunks.Add(new AddComponentChunkOp
                            {
                                Chunks = batch.ActiveArchetypeChunks.Ptr,
                                Count = batch.ActiveArchetypeChunks.Length,
                                TypeIndex = data.DisabledTypeIndex,
                            });
                        }
                        continue;
                    }

                    var capacity = batch.Archetype.ChunkCapacity;
                    var requiredFullChunks = requiredEntities / capacity;
                    var fitsExactlyInChunkCapacity = requiredEntities % capacity == 0;
                    var requiredPartialChunks = fitsExactlyInChunkCapacity ? 0 : 1;
                    var requiredChunks = requiredFullChunks + requiredPartialChunks;
                    var remainingEntities = requiredEntities;
                    var remainingInactiveFulls = batch.InactiveFullArchetypeChunks.Length;

                    if (requiredFullChunks > 0)
                    {
                        var remainingFullChunks = requiredFullChunks;

                        // Keep full active chunks
                        if (batch.ActiveFullArchetypeChunks.Length != 0)
                        {
                            var kept = math.min(remainingFullChunks, batch.ActiveFullArchetypeChunks.Length);
                            remainingFullChunks -= kept;

                            var excessActiveChunks = batch.ActiveFullArchetypeChunks.Length - kept;
                            if (excessActiveChunks != 0)
                            {
                                // Deactivate excess
                                data.StructuralChanges.AddComponentToChunks.Add(new AddComponentChunkOp
                                {
                                    Chunks = batch.ActiveFullArchetypeChunks.Ptr,
                                    Count = excessActiveChunks,
                                    TypeIndex = data.DisabledTypeIndex,
                                });
                            }
                        }

                        if (batch.ActivePartialArchetypeChunk.Length != 0)
                        {
                            // Deactivate partial chunks
                            data.StructuralChanges.AddComponentToChunks.Add(new AddComponentChunkOp
                            {
                                Chunks = batch.ActivePartialArchetypeChunk.Ptr,
                                Count = batch.ActivePartialArchetypeChunk.Length,
                                TypeIndex = data.DisabledTypeIndex,
                            });
                        }

                        // Activate inactive full chunks
                        if (remainingFullChunks > 0 && remainingInactiveFulls != 0)
                        {
                            var conversionCount = math.min(remainingFullChunks, batch.InactiveFullArchetypeChunks.Length);
                            data.StructuralChanges.RemoveComponentFromChunks.Add(new RemoveComponentChunkOp
                            {
                                Chunks = batch.InactiveFullArchetypeChunks.Ptr,
                                TypeIndex = data.DisabledTypeIndex,
                                Count = conversionCount,
                            });
                            remainingInactiveFulls -= conversionCount;
                            remainingFullChunks -= conversionCount;
                        }

                        remainingEntities -= (requiredFullChunks - remainingFullChunks) * capacity;
                    }
                    else
                    {
                        // Deactivate all active chunks
                        data.StructuralChanges.AddComponentToChunks.Add(new AddComponentChunkOp
                        {
                            Chunks = batch.ActiveArchetypeChunks.Ptr,
                            Count = batch.ActiveArchetypeChunks.Length,
                            TypeIndex = data.DisabledTypeIndex,
                        });
                    }

                    if (remainingEntities > 0 && batch.InactiveChunks.Length != 0)
                    {
                        // Create from inactive partials
                        if (batch.InactivePartialArchetypeChunk.Length != 0) // todo create full chunk sized pieces out before this
                        {
                            var batchInChunks = new NativeList<EntityBatchInChunkProxy>(Allocator.Temp);
                            for (int j = 0; j < batch.InactivePartialArchetypeChunk.Length; j++)
                            {
                                var archetypeChunkPtr = ((ArchetypeChunk*)batch.InactivePartialArchetypeChunk.Ptr)[j];
                                var amountToMove = math.min(archetypeChunkPtr.Count, remainingEntities);
                                var entityBatch = new EntityBatchInChunkProxy
                                {
                                    ChunkPtr = archetypeChunkPtr.GetChunkPtr(),
                                    Count = amountToMove,
                                    StartIndex = 0,
                                };
                                batchInChunks.Add(entityBatch);
                                remainingEntities -= amountToMove;
                                if (remainingEntities == 0)
                                    break;
                            }

                            data.StructuralChanges.RemoveComponentBatches.Add(new RemoveComponentBatchOp
                            {
                                EntityBatches = batchInChunks.AsParallelWriter().ListData,
                                TypeIndex = data.DisabledTypeIndex,
                            });
                        }

                        // Create from inactive fulls
                        if (remainingEntities > 0 && remainingInactiveFulls != 0)
                        {
                            var batchInChunks = new NativeList<EntityBatchInChunkProxy>(Allocator.Temp);
                            for (int j = remainingInactiveFulls - 1; j == -1; j--)
                            {
                                var archetypeChunk = ((ArchetypeChunk*)batch.InactiveFullArchetypeChunks.Ptr)[j];
                                var amountToMove = math.min(archetypeChunk.Count, remainingEntities);
                                var entityBatch = new EntityBatchInChunkProxy
                                {
                                    ChunkPtr = archetypeChunk.GetChunkPtr(),
                                    Count = amountToMove,
                                    StartIndex = 0,
                                };
                                batchInChunks.Add(entityBatch);
                                remainingEntities -= amountToMove;
                                if (remainingEntities == 0)
                                    break;
                            }

                            data.StructuralChanges.RemoveComponentBatches.Add(new RemoveComponentBatchOp
                            {
                                EntityBatches = batchInChunks.AsParallelWriter().ListData,
                                TypeIndex = data.DisabledTypeIndex,
                            });
                        }
                    }

                    if (remainingEntities != 0) // todo: currently not copying from active partials to avoid create.
                    {
                        data.StructuralChanges.CreateChunks.Add(new CreateChunksOp
                        {
                            Archetype = batch.Archetype,
                            EntityCount = remainingEntities,
                        });
                    }
                }

            }).Run();
        }

        internal void SetComponents()
        {
            var batchCount = Data.Batches.Length;
            var batchesPtr = (byte*)Data.Batches.Ptr;

            Job.WithCode(() =>
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EventBatch* batch = (EventBatch*)(batchesPtr + sizeof(EventBatch) * i);

                    if (batch->EntityCount == 0)
                        continue;

                    MultiAppendBuffer.Reader metaComponents = batch->ComponentQueue.GetMetaReader();

                    for (int j = 0; j < batch->Archetype.ChunkCount; j++)
                    {
                        ArchetypeChunk* archetypeChunkPtr = batch->ActiveChunks.GetArchetypeChunkPtr(j);
                        byte* chunkPtr = (byte*)archetypeChunkPtr->GetChunkPtr();
                        var size = archetypeChunkPtr->Count * sizeof(EntityEvent);
                        metaComponents.CopyTo(chunkPtr + batch->Offsets.MetaOffset, size);
                    }

                    if (batch->HasComponent)
                    {
                        MultiAppendBuffer.Reader queuedComponents = batch->ComponentQueue.GetComponentReader();

                        for (int j = 0; j < batch->Archetype.ChunkCount; j++)
                        {
                            ArchetypeChunk* archetypeChunkPtr = batch->ActiveChunks.GetArchetypeChunkPtr(j);
                            byte* chunkPtr = (byte*)archetypeChunkPtr->GetChunkPtr();
                            var size = archetypeChunkPtr->Count * batch->ComponentTypeSize;
                            queuedComponents.CopyTo(chunkPtr + batch->Offsets.ComponentOffset, size);
                        }
                    }

                    if (batch->HasBuffer)
                    {
                        MultiAppendBuffer.Reader links = batch->ComponentQueue.GetLinksReader();

                        for (int j = 0; j < batch->Archetype.ChunkCount; j++)
                        {
                            ArchetypeChunk chunk = ((ArchetypeChunk*)batch->ActiveArchetypeChunks.Ptr)[j];
                            var entityCount = chunk.Count;

                            byte* chunkBufferHeaders = batch->GetBufferPointer(chunk);
                            byte* chunkLinks = batch->GetBufferLinkPointer(chunk);

                            // Can't assume that buffer data is stored int he same order as the primary event component
                            // due to parallel writing. Can't store the buffer and component sequentially because it 
                            // would prevent a contiguous copy of all component data in to the chunk. Can't store buffer pointers
                            // in 'BufferHeader's at enqueue time and then copy them all at once into chunk because of
                            // dynamic allocation size (the pointer may change); it can't be a pointer to a pointer because
                            // the operation of BufferHeader/DynamicBuffer is not somethign we control; if you have to patch
                            // the BufferHeader pointer then you're iterating every entity anyway.

                            links.CopyTo(chunkLinks, entityCount * UnsafeUtility.SizeOf<BufferLink>());

                            for (int x = 0; x < entityCount; x++)
                            {
                                BufferHeaderProxy* bufferHeader = (BufferHeaderProxy*)(chunkBufferHeaders + x * batch->BufferSizeInChunk);
                                BufferLink* link = (BufferLink*)(chunkLinks + x * UnsafeUtility.SizeOf<BufferLink>());

                                ref var source = ref batch->ComponentQueue._bufferData.GetBuffer(link->ThreadIndex);
                                BufferHeaderProxy.Assign(bufferHeader, source.Ptr + link->Offset, link->Length, batch->BufferElementSize, batch->BufferAlignmentInBytes, default, default);
                            }
                        }
                    }
                }

            }).Run();
        }

        internal void UpdateChunkCollections() // This is just seperated out for benchmarking
        {
            var batchCount = Data.Batches.Length;
            var batchesPtr = (byte*)Data.Batches.Ptr;

            Job.WithCode(() =>
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EventBatch* batch = (EventBatch*)(batchesPtr + sizeof(EventBatch) * i);
                    batch->UpdateChunkCollections();
                }

            }).Run();
        }

        internal void ClearQueues() // This is just seperated out for benchmarking
        {
            var batchCount = Data.Batches.Length;
            var batchesPtr = (byte*)Data.Batches.Ptr;

            Job.WithCode(() =>
            {
                for (int i = 0; i < batchCount; i++)
                {
                    EventBatch* batch = (EventBatch*)(batchesPtr + sizeof(EventBatch) * i);
                    batch->ComponentQueue.Clear();
                }

            }).Run();
        }

        /// <summary>
        /// Add an event to the default EventQueue.
        /// </summary>
        public int Enqueue<T>(T item = default) where T : struct, IComponentData
        {
            return GetQueue<T>().Enqueue(item);
        }

        /// <summary>
        /// Add an event with both a component and a buffer on the same entity, to the default EventQueue.
        /// </summary>
        /// <typeparam name="TComponent"></typeparam>
        /// <typeparam name="TBufferData"></typeparam>
        /// <param name="item">the event component</param>
        /// <param name="items">the buffer element data</param>
        public unsafe int Enqueue<TComponent, TBufferData>(TComponent item, NativeArray<TBufferData> items)
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            return GetQueue<TComponent, TBufferData>().Enqueue(item, items.GetUnsafePtr(), items.Length);
        }

        /// <summary>
        /// Add an event with both a component and a buffer on the same entity, to the default EventQueue.
        /// </summary>
        /// <typeparam name="TComponent"></typeparam>
        /// <typeparam name="TBufferData"></typeparam>
        /// <param name="item">the event component</param>
        /// <param name="items">the buffer element data</param>
        /// <param name="length">the number of buffer elements</param>
        public unsafe int Enqueue<TComponent, TBufferData>(TComponent item, TBufferData* items, int length)
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            return GetQueue<TComponent, TBufferData>().Enqueue(item, items, length);
        }

        /// <summary>
        /// Acquire a the shared queue for creating events within jobs.
        /// </summary>
        public EventQueue<T> GetQueue<T>(int startingPoolSize = 0) where T : struct, IComponentData
        {
            return GetOrCreateBatch(TypeManager.GetTypeInfo<T>(), default, startingPoolSize).Batch.ComponentQueue.Cast<EventQueue<T>>();
        }

        public EventBufferQueue<T> GetBufferQueue<T>(int startingPoolSize = 0) where T : unmanaged, IBufferElementData
        {
            return GetOrCreateBatch(default, TypeManager.GetTypeInfo<T>(), startingPoolSize).Batch.ComponentQueue.Cast<EventBufferQueue<T>>();
        }

        /// <summary>
        /// Acquire a queue for creating events that have both a component and buffer on the same element.
        /// </summary>
        /// <typeparam name="TComponent">the event component</typeparam>
        /// <typeparam name="TBufferData">the buffer element data</typeparam>
        /// <returns>an <see cref="EventQueue{T1, T2}"/> struct that can queue events to be created</returns>
        public EventQueue<TComponent, TBufferData> GetQueue<TComponent, TBufferData>(int startingPoolSize = 0)
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            var t1 = TypeManager.GetTypeInfo<TComponent>();
            var t2 = TypeManager.GetTypeInfo<TBufferData>();
            return GetOrCreateBatch(t1,t2, startingPoolSize).Batch.ComponentQueue.Cast<EventQueue<TComponent, TBufferData>>();
        }

        /// <summary>
        /// Acquire an untyped shared queue for creating events within jobs.
        /// </summary>
        public EventQueue GetQueue(TypeManager.TypeInfo typeInfo, TypeManager.TypeInfo bufferTypeInfo = default)
        {
            return GetOrCreateBatch(typeInfo, bufferTypeInfo).Batch.ComponentQueue;
        }

        /// <summary>
        /// Acquire an untyped shared queue for creating events within jobs.
        /// </summary>
        public EventQueue GetQueue(ComponentType type, ComponentType bufferType = default)
        {
            return GetOrCreateBatch(TypeManager.GetTypeInfo(bufferType.TypeIndex), TypeManager.GetTypeInfo(bufferType.TypeIndex)).Batch.ComponentQueue;
        }

        internal (EventBatch Batch, bool WasCreated) GetOrCreateBatch(TypeManager.TypeInfo typeInfo, TypeManager.TypeInfo bufferTypeInfo = default, int startingPoolSize = 0)
        {
            int key = GetKey(typeInfo.TypeIndex, bufferTypeInfo.TypeIndex);
            if (!Data.TypeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = new EventBatch(EntityManager, new EventBatch.EventDefinition
                {
                    MetaType = Data.EventComponent,
                    StartingPoolSize = startingPoolSize,
                    ComponentTypeInfo = typeInfo,
                    BufferTypeInfo = bufferTypeInfo,
                });

                index = Data.BatchCount++;
                Data.Batches.Resize(Data.BatchCount);
                Data.Batches.Ptr[index] = batch;
                Data.TypeIndexToBatchMap[key] = index;
                return (batch, true);
            }
            return (Data.Batches.Ptr[index], false);
        }

        private int GetKey(int typeIndex1, int typeIndex2)
        {
            int hash = 23;
            hash = hash * 31 + typeIndex1;
            hash = hash * 31 + typeIndex2;
            return hash;
        }

        protected override void OnDestroy()
        {
            for (int i = 0; i < Data.Batches.Length; i++)
                Data.Batches.Ptr[i].Dispose();

            Data.Batches.Dispose();
            Data.TypeIndexToBatchMap.Dispose();
            Data.StructuralChanges.Dispose();
            UnsafeUtility.Free(_dataPtr, Allocator.Persistent);
        }

    }

    public sealed class EntityEventSystemDebugView
    {
        private EntityEventSystem _actual;

        public EntityEventSystemDebugView(EntityEventSystem input)
        {
            _actual = input;
        }

        internal unsafe EventBatch[] Batches
        {
            get
            {
                var length = _actual.Data.Batches.Length;
                var result = new EventBatch[length];
                for (int i = 0; i < length; i++)
                    result[i] = _actual.Data.Batches.Ptr[i];
                return result;
            }
        }

        internal unsafe EventQueue[] ComponentQueues
        {
            get
            {
                var length = _actual.Data.Batches.Length;
                var result = new EventQueue[length];
                for (int i = 0; i < length; i++)
                    result[i] = _actual.Data.Batches.Ptr[i].ComponentQueue;
                return result;
            }
        }

        public ComponentType EventComponent => _actual.Data.EventComponent;

        public int BatchCount => _actual.Data.BatchCount;

        public int QueuedEventCount => Batches.Sum(b => b.ComponentQueue.Count());

    }

}

