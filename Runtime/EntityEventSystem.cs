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
        private UnsafeHashMap<int, EventArchetype> _typeIndexToBatchMap;
        private ComponentType _eventComponent;
        private EntityQuery _allEventsQuery;
        private NativeList<EventArchetype> _batches;
        private NativeList<EventArchetype> _buffer;
        private UnsafeList<Entity> _entities;
        private UnsafeNativeArray _slicer;
        private ArchetypeChunkComponentType<EntityEvent> _archetypeComponentTypeStub;

        protected override void OnCreate()
        {
            var typeCount = TypeManager.GetTypeCount();
            _typeIndexToBatchMap = new UnsafeHashMap<int, EventArchetype>(typeCount, Allocator.Persistent);
            _eventComponent = ComponentType.ReadOnly<EntityEvent>();
            _allEventsQuery = EntityManager.CreateEntityQuery(_eventComponent);
            _batches = new NativeList<EventArchetype>(typeCount, Allocator.Persistent);
            _buffer = new NativeList<EventArchetype>(typeCount, Allocator.Persistent);
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

            var batchesToProcess = _buffer;
            var mapPtr = UnsafeUtility.AddressOf(ref _typeIndexToBatchMap);
            var batchesPtr = UnsafeUtility.AddressOf(ref _batches);
            var entitiesPtr = UnsafeUtility.AddressOf(ref _entities);
            var slicerPtr = UnsafeUtility.AddressOf(ref _slicer);

            Job.WithCode(() =>
            {
                ref var map = ref UnsafeUtilityEx.AsRef<UnsafeHashMap<int, EventArchetype>>(mapPtr);
                ref var batches = ref UnsafeUtilityEx.AsRef<NativeList<EventArchetype>>(batchesPtr);
                ref var entities = ref UnsafeUtilityEx.AsRef<UnsafeList<Entity>>(entitiesPtr);
                ref var slicer = ref UnsafeUtilityEx.AsRef<UnsafeNativeArray>(slicerPtr);

                if (batches.Length < mapCount)
                {
                    var values = map.GetValueArray(Allocator.Temp);
                    batches.Clear();
                    batches.AddRange(values.GetUnsafePtr(), values.Length);
                }

                batchesToProcess.Clear();
                var ptr = batches.GetUnsafePtr();

                int total = 0;
                for (int i = 0; i < mapCount; i++)
                {
                    ref var archetype = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);
                    var count = archetype.ComponentQueue.ComponentCount();
                    if (count != 0)
                    {
                        batchesToProcess.Add(archetype);
                        total += count;
                    }
                }

                if (entities.Capacity < total)
                {
                    entities.Resize(total);
                    slicer.m_Buffer = entities.Ptr;
                }

                entities.Length = total;
                slicer.m_Length = total;
                slicer.m_MaxIndex = total - 1;

            }).Run();

            var created = 0;
            for (int i = 0; i < _buffer.Length; i++)
            {
                var batch = _buffer[i];
                var batchCount = batch.ComponentQueue.CachedCount;
                var arr = _slicer.Slice<Entity>(created, batchCount);
                EntityManager.CreateEntity(batch.Archetype, arr);
                created += batchCount;
            }

            Job.WithCode(() =>
            {
                var ptr = batchesToProcess.GetUnsafePtr();

                for (int i = 0; i < batchesToProcess.Length; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);

                    var chunks = new NativeArray<ArchetypeChunk>(batch.Archetype.ChunkCount, Allocator.TempJob);
                    batch.Archetype.CopyChunksTo(chunks);

                    MultiAppendBuffer.Reader components = batch.ComponentQueue.GetComponentReader();

                    for (int j = 0; j < chunks.Length; j++)
                    {
                        var chunk = chunks[j];
                        components.CopyTo(batch.GetComponentPointer(chunk), chunk.Count * batch.ComponentTypeSize);
                    }

                    if (batch.HasBuffer)
                    {
                        MultiAppendBuffer.Reader links = batch.ComponentQueue.GetLinksReader();

                        for (int j = 0; j < chunks.Length; j++)
                        {
                            ArchetypeChunk chunk = chunks[j];
                            byte* chunkBufferHeaders = batch.GetBufferPointer(chunk);
                            byte* chunkLinks = batch.GetBufferLinkPointer(chunk);

                            links.CopyTo(chunkLinks, chunk.Count * UnsafeUtility.SizeOf<BufferLink>());

                            for (int x = 0; x < chunk.Count; x++)
                            {
                                BufferHeaderProxy* bufferHeader = (BufferHeaderProxy*)(chunkBufferHeaders + x * batch.BufferTypeInfo.SizeInChunk);
                                BufferLink* link = (BufferLink*)(chunkLinks + x * UnsafeUtility.SizeOf<BufferLink>());

                                ref var source = ref batch.ComponentQueue._bufferData.GetBuffer(link->ThreadIndex);
                                BufferHeaderProxy.Assign(bufferHeader, source.Ptr + link->Offset, link->Length, batch.BufferTypeInfo.ElementSize, batch.BufferTypeInfo.AlignmentInBytes, default, default);
                            }
                        }
                    }

                    chunks.Dispose();

                    batch.ComponentQueue.Clear();
                }

            }).Run();

        }
        
        /// <summary>
        /// Add an event to the default EventQueue.
        /// </summary>
        public void Enqueue<T>(T item = default) where T : struct, IComponentData
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

        private EventArchetype GetOrCreateBatch<T>() where T : struct, IComponentData
        {
            int key = TypeManager.GetTypeIndex<T>();
            if (!_typeIndexToBatchMap.TryGetValue(key, out EventArchetype batch))
            {
                batch = EventArchetype.Create<T>(EntityManager, _eventComponent, Allocator.Persistent);
                _typeIndexToBatchMap[key] = batch;
            }
            return batch;
        }

        public EventArchetype GetOrCreateBatch(TypeManager.TypeInfo typeInfo)
        {
            int key = typeInfo.TypeIndex;
            if (!_typeIndexToBatchMap.TryGetValue(key, out EventArchetype batch))
            {
                batch = EventArchetype.Create(EntityManager, _eventComponent, typeInfo, Allocator.Persistent);
                _typeIndexToBatchMap[key] = batch;
            }
            return batch;
        }

        private EventArchetype GetOrCreateBatch<TComponent,TBufferData>()
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            var key = GetHashCode<TComponent, TBufferData>();
            if (!_typeIndexToBatchMap.TryGetValue(key, out EventArchetype batch))
            {
                batch = EventArchetype.Create<TComponent,TBufferData>(EntityManager, _eventComponent, Allocator.Persistent);
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
