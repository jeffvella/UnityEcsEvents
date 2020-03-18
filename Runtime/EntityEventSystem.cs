using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using System.Reflection;
using System;
using UnityEngine;
using Unity.Profiling;

namespace Vella.Events
{
    public unsafe class EntityEventSystem : SystemBase
    {
        private UnsafeHashMap<int, EventBatch> _typeIndexToBatchMap;
        private ComponentType _eventComponent;
        private EntityQuery _allEventsQuery;
        private NativeList<EventBatch> _batches;
        private NativeList<EventBatch> _buffer;
        private UnsafeList<Entity> _entities;
        private UnsafeNativeArray _slicer;
        private ArchetypeChunkComponentType<EntityEvent> _archetypeComponentTypeStub;

        protected override void OnCreate()
        {
            var typeCount = TypeManager.GetTypeCount();
            _typeIndexToBatchMap = new UnsafeHashMap<int, EventBatch>(typeCount, Allocator.Persistent);
            _eventComponent = ComponentType.ReadOnly<EntityEvent>();
            _allEventsQuery = EntityManager.CreateEntityQuery(_eventComponent);
            _batches = new NativeList<EventBatch>(typeCount, Allocator.Persistent);
            _buffer = new NativeList<EventBatch>(typeCount, Allocator.Persistent);
            _entities = new UnsafeList<Entity>(typeCount, Allocator.Persistent);
            _slicer = _entities.ToUnsafeNativeArray();
        }

        protected override void OnDestroy()
        {
            var batches = _typeIndexToBatchMap.GetValueArray(Allocator.Temp);
            for (int i = 0; i < batches.Length; i++)
                batches[i].Dispose();
            batches.Dispose();

            _typeIndexToBatchMap.Dispose();
            _batches.Dispose();
            _buffer.Dispose();
            _entities.Dispose();
        }

        protected override void OnStartRunning()
        {
            _archetypeComponentTypeStub = GetArchetypeChunkComponentType<EntityEvent>();
        }

        protected unsafe override void OnUpdate()
        {
            var mapCount = _typeIndexToBatchMap.Count();
            if (mapCount == 0)
                return;

            if (_entities.Length != 0)
            {
                if (_entities.Length < 1000)
                {
                    EntityManager.DestroyEntity(_slicer.AsNativeArray<Entity>());
                }
                else
                {
                    EntityManager.DestroyEntity(_allEventsQuery);
                } 
            }

            var mapPtr = UnsafeUtility.AddressOf(ref _typeIndexToBatchMap);
            var batchesPtr = UnsafeUtility.AddressOf(ref _batches);
            var batchesToProcess = _buffer;
            var total = 0;
            var totalPtr = &total;

            Job.WithCode(() =>
            {
                ref var map = ref UnsafeUtilityEx.AsRef<UnsafeHashMap<int, EventBatch>>(mapPtr);
                ref var batches = ref UnsafeUtilityEx.AsRef<NativeList<EventBatch>>(batchesPtr);
                ref var eventTotal = ref UnsafeUtilityEx.AsRef<int>(totalPtr);

                if (batches.Length < mapCount)
                {
                    var values = map.GetValueArray(Allocator.Temp);
                    batches.Clear();
                    batches.AddRange(values.GetUnsafePtr(), values.Length);
                }

                batchesToProcess.Clear();
                var ptr = batches.GetUnsafePtr();

                for (int i = 0; i < mapCount; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventBatch>(ptr, i);
                    var count = batch.ComponentQueue.ComponentCount();
                    if (count != 0)
                    {
                        batchesToProcess.Add(batch);
                        eventTotal += count;
                    }
                }

            }).Run();

            if (_entities.Capacity < total)
            {
                _entities.Resize(total);
                _slicer.m_Buffer = _entities.Ptr;
            }
            _entities.Length = total;
            _slicer.m_Length = total;
            _slicer.m_MaxIndex = total - 1;

            var created = 0;
            for (int i = 0; i < _buffer.Length; i++)
            {
                var batch = _buffer[i];
                var batchCount = batch.ComponentQueue.CachedCount;
                var arr = _slicer.Slice<Entity>(created, batchCount);
                EntityManager.CreateEntity(batch.Archetype, arr);
                created += batchCount;
            }

            var componentTypeStub = _archetypeComponentTypeStub;

            Job.WithCode(() =>
            {
                var ptr = batchesToProcess.GetUnsafePtr();

                for (int i = 0; i < batchesToProcess.Length; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventBatch>(ptr, i);

                    var chunks = new NativeArray<ArchetypeChunk>(batch.Archetype.ChunkCount, Allocator.Temp);
                    batch.Archetype.CopyChunksTo(chunks);

                    var componentType = componentTypeStub;
                    UnsafeUtility.CopyStructureToPtr(ref batch.ComponentTypeIndex, UnsafeUtility.AddressOf(ref componentType));
                    MultiAppendBuffer.Reader components = batch.ComponentQueue.GetComponentReader();

                    for (int j = 0; j < chunks.Length; j++)
                    {
                        var chunk = chunks[j];

                        var componentPtr = (byte*)chunk.GetNativeArray(componentType).GetUnsafeReadOnlyPtr();
                        components.CopyTo(componentPtr, chunk.Count * batch.ComponentTypeSize);

                        //var debugs = chunk.GetNativeArray(debugComponentType);
                        //for (int z = 0; z < debugs.Length; z++)
                        //{
                        //    debugs[z] = new EventDebugInfo
                        //    {
                        //        ChunkIndex = j,
                        //        IndexInChunk = z,
                        //    };
                        //}
                    }

                    if (batch.HasBuffer)
                    {
                        var linkType = componentTypeStub;
                        UnsafeUtility.CopyStructureToPtr(ref batch.BufferLinkTypeIndex, UnsafeUtility.AddressOf(ref linkType));
                        MultiAppendBuffer.Reader links = batch.ComponentQueue.GetLinksReader();

                        var bufferType = componentTypeStub;
                        UnsafeUtility.CopyStructureToPtr(ref batch.BufferTypeIndex, UnsafeUtility.AddressOf(ref bufferType));

                        for (int j = 0; j < chunks.Length; j++)
                        {
                            var chunk = chunks[j];

                            BufferHeaderProxy* bufferHeaderPtr = (BufferHeaderProxy*)chunk.GetNativeArray(bufferType).GetUnsafeReadOnlyPtr();
                            BufferLink* linkPtr = (BufferLink*)chunk.GetNativeArray(linkType).GetUnsafeReadOnlyPtr();
                            links.CopyTo(linkPtr, chunk.Count * UnsafeUtility.SizeOf<BufferLink>());

                            for (int x = 0; x < chunk.Count; x++)
                            {
                                BufferHeaderProxy* bufferHeader = (BufferHeaderProxy*)((byte*)bufferHeaderPtr + x * batch.BufferTypeInfo.SizeInChunk);
                                BufferLink* link = (BufferLink*)((byte*)linkPtr + x * UnsafeUtility.SizeOf<BufferLink>());

                                ref var source = ref batch.ComponentQueue._bufferData.GetBuffer(link->ThreadIndex);
                                BufferHeaderProxy.Assign(bufferHeader, source.Ptr + link->Offset, link->Length, batch.BufferTypeInfo.ElementSize, batch.BufferTypeInfo.AlignmentInBytes, default, default);
                            }
                        }
                    }

                    batch.ComponentQueue.Clear();
                }

            }).Run();

        }
        
        /// <summary>
        /// Add an event to the default EventQueue.
        /// </summary>
        public void Enqueue<T>(T item) where T : struct, IComponentData
        {
            GetQueue<T>().Enqueue(item);
        }

        /// <summary>
        /// Add an event with both a component and a buffer on the same entity, to the default EventQueue.
        /// </summary>
        /// <typeparam name="TComponent"></typeparam>
        /// <typeparam name="TBufferData"></typeparam>
        /// <param name="item">the event component</param>
        /// <param name="items">the buffer element data</param>
        public unsafe void Enqueue<TComponent, TBufferData>(TComponent item, NativeArray<TBufferData> items)
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            var queue = GetQueue<TComponent, TBufferData>();
            queue.Enqueue(item, items.GetUnsafePtr(), items.Length);
        }

        /// <summary>
        /// Add an event with both a component and a buffer on the same entity, to the default EventQueue.
        /// </summary>
        /// <typeparam name="TComponent"></typeparam>
        /// <typeparam name="TBufferData"></typeparam>
        /// <param name="item">the event component</param>
        /// <param name="items">the buffer element data</param>
        /// <param name="length">the number of buffer elements</param>
        public unsafe void Enqueue<TComponent, TBufferData>(TComponent item, TBufferData* items, int length) 
            where TComponent : struct, IComponentData 
            where TBufferData : unmanaged, IBufferElementData
        {
            var queue = GetQueue<TComponent,TBufferData>();
            queue.Enqueue(item, items, length);
        }

        /// <summary>
        /// Acquire a the shared queue for creating events within jobs.
        /// </summary>
        public EventQueue<T> GetQueue<T>() where T : struct, IComponentData
        {
            return GetOrCreateBatch<T>().ComponentQueue.Cast<EventQueue<T>>();
        }

        /// <summary>
        /// Acquire an untyped shared queue for creating events within jobs.
        /// </summary>
        public EventQueue GetQueue(TypeManager.TypeInfo typeInfo)
        {
            return GetOrCreateBatch(typeInfo).ComponentQueue;
        }

        /// <summary>
        /// Acquire a queue for creating events that have both a component and buffer on the same element.
        /// </summary>
        /// <typeparam name="TComponent">the event component</typeparam>
        /// <typeparam name="TBufferData">the buffer element data</typeparam>
        /// <returns>an <see cref="EventQueue{T1, T2}"/> struct that can queue events to be created</returns>
        public EventQueue<TComponent,TBufferData> GetQueue<TComponent,TBufferData>() 
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            return GetOrCreateBatch<TComponent,TBufferData>().ComponentQueue.Cast<EventQueue<TComponent,TBufferData>>();
        }

        private EventBatch GetOrCreateBatch<T>() where T : struct, IComponentData
        {
            int key = TypeManager.GetTypeIndex<T>();
            if (!_typeIndexToBatchMap.TryGetValue(key, out EventBatch batch))
            {
                batch = EventBatch.Create<T>(EntityManager, _eventComponent, Allocator.Persistent);
                _typeIndexToBatchMap[key] = batch;
            }
            return batch;
        }

        public EventBatch GetOrCreateBatch(TypeManager.TypeInfo typeInfo)
        {
            int key = typeInfo.TypeIndex;
            if (!_typeIndexToBatchMap.TryGetValue(key, out EventBatch batch))
            {
                batch = EventBatch.Create(EntityManager, _eventComponent, typeInfo, Allocator.Persistent);
                _typeIndexToBatchMap[key] = batch;
            }
            return batch;
        }

        private EventBatch GetOrCreateBatch<TComponent,TBufferData>()
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            var key = GetHashCode<TComponent, TBufferData>();
            if (!_typeIndexToBatchMap.TryGetValue(key, out EventBatch batch))
            {
                batch = EventBatch.Create<TComponent,TBufferData>(EntityManager, _eventComponent, Allocator.Persistent);
                _typeIndexToBatchMap[key] = batch;
            }
            return batch;
        }

        private int GetHashCode<T1,T2>()
        {
            int hash = 23;
            hash = hash * 31 + TypeManager.GetTypeIndex<T1>();
            hash = hash * 31 + TypeManager.GetTypeIndex<T2>();
            return hash;
        }

    }

    public struct EventDebugInfo : IComponentData
    {
        public int ChunkIndex;
        public int IndexInChunk;
    }

}