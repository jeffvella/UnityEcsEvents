using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Vella.Events
{
    public class EntityEventSystem : SystemBase
    {
        private UnsafeHashMap<int, EventBatch> _typeIndexToBatchMap;
        private ComponentType _eventComponent;
        private EntityQuery _allEventsQuery;
        private NativeList<EventBatch> _batches;
        private NativeList<EventBatch> _buffer;
        protected override void OnCreate()
        {
            var typeCount = TypeManager.GetTypeCount();
            _typeIndexToBatchMap = new UnsafeHashMap<int, EventBatch>(typeCount, Allocator.Persistent);
            _eventComponent = ComponentType.ReadOnly<EntityEvent>();
            _allEventsQuery = EntityManager.CreateEntityQuery(_eventComponent);
            _batches = new NativeList<EventBatch>(typeCount, Allocator.Persistent);
            _buffer = new NativeList<EventBatch>(typeCount, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            var batches = _typeIndexToBatchMap.GetValueArray(Allocator.Temp);
            for (int i = 0; i < batches.Length; i++)
                batches[i].Dispose();
            batches.Dispose();
            _typeIndexToBatchMap.Dispose();
        }

        protected unsafe override void OnUpdate()
        {
            if (_typeIndexToBatchMap.Length == 0)
                return;

            EntityManager.DestroyEntity(_allEventsQuery);

            var mapPtr = UnsafeUtility.AddressOf(ref _typeIndexToBatchMap);
            var batchesPtr = UnsafeUtility.AddressOf(ref _batches);
            var batchesToProcess = _buffer;

            Job.WithCode(() =>
            {
                ref var map = ref UnsafeUtilityEx.AsRef<UnsafeHashMap<int, EventBatch>>(mapPtr);
                ref var batches = ref UnsafeUtilityEx.AsRef<NativeList<EventBatch>>(batchesPtr);

                if (batches.Length < map.Length)
                {
                    var values = map.GetValueArray(Allocator.Temp);
                    batches.Clear();
                    batches.AddRangeNoResize(values.GetUnsafePtr(), values.Length);
                }

                batchesToProcess.Clear();
                var ptr = batches.GetUnsafePtr();
                for (int i = 0; i < map.Length; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventBatch>(ptr, i);
                    if (batch.ComponentQueue.ComponentCount() != 0)
                    {
                        batchesToProcess.Add(batch);
                    }
                }

            }).Run();

            for (int i = 0; i < _buffer.Length; i++)
            {
                EntityManager.CreateEntity(_buffer[i].Archetype, _buffer[i].ComponentQueue.CachedCount, Allocator.Temp);
            }

            var entityEventComponentType = GetArchetypeChunkComponentType<EntityEvent>();
            var entityEventBufferType = GetArchetypeChunkBufferType<TempEntityBuffer>();

            Job.WithCode(() =>
            {
                var ptr = batchesToProcess.GetUnsafePtr();

                for (int i = 0; i < batchesToProcess.Length; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventBatch>(ptr, i);

                    // Get all the chunks for this archetype
                    var chunks = new NativeArray<ArchetypeChunk>(batch.Archetype.ChunkCount, Allocator.Temp);
                    batch.Archetype.CopyChunksTo(chunks);

                    var componentType = entityEventComponentType;
                    UnsafeUtility.CopyStructureToPtr(ref batch.ComponentTypeIndex, UnsafeUtility.AddressOf(ref componentType));
                    UnsafeMultiAppendBuffer.Reader components = batch.ComponentQueue.GetComponentReader();

                    var linkType = entityEventComponentType;
                    UnsafeUtility.CopyStructureToPtr(ref batch.BufferLinkTypeIndex, UnsafeUtility.AddressOf(ref linkType));
                    UnsafeMultiAppendBuffer.Reader links = batch.ComponentQueue.GetLinksReader();



                    for (int j = 0; j < chunks.Length; j++)
                    {
                        var chunk = chunks[j];

                        // todo cache the ptrs directly to the component blocks.
                        var componentPtr = (byte*)chunk.GetNativeArray(componentType).GetUnsafeReadOnlyPtr();
                        components.CopyTo(componentPtr, chunk.Count * batch.ComponentTypeSize);

                        // Since all events were destroyed at the start of OnUpdate(), 
                        // the only entities in these chunks should be the ones we just created.

                        //batch.ComponentQueue.CopyComponentsTo(componentPtr, chunk.Count * batch.ComponentTypeSize);

                        var linksPtr = (byte*)chunk.GetNativeArray(linkType).GetUnsafeReadOnlyPtr();
                        links.CopyTo(linksPtr, chunk.Count * UnsafeUtility.SizeOf<BufferLink>());
                    }



                    var bufferTypeType = entityEventComponentType;
                    UnsafeUtility.CopyStructureToPtr(ref batch.BufferTypeIndex, UnsafeUtility.AddressOf(ref bufferTypeType));

                    for (int j = 0; j < chunks.Length; j++)
                    {
                        var chunk = chunks[j];

                        //BufferLink* linkPtr = (BufferLink*)chunk.GetNativeArray(linkType).GetUnsafeReadOnlyPtr();
                        //batch.ComponentQueue.CopyLinksTo(linkPtr, chunk.Count);

                        //if (batch.HasBuffer)
                        //{
                        //    var bufferHeaders = chunk.GetNativeArray(bufferTypeType);
                        //    var bufferHeaderPtr = (BufferHeaderProxy*)bufferHeaders.GetUnsafeReadOnlyPtr();

                        //    for (int x = 0; x < chunk.Count; x++)
                        //    {
                        //        BufferHeaderProxy* destination = (BufferHeaderProxy*)((byte*)bufferHeaderPtr + x * sizeof(BufferHeaderProxy));

                        //        BufferLink* link = (BufferLink*)((byte*)linkPtr + x * sizeof(BufferLink));

                        //        ref var source = ref batch.ComponentQueue._bufferData.GetBuffer(link->ThreadIndex);

                        //        // todo get/store memory init pattern from EntityComponentStore
                        //        //BufferHeaderProxy.Assign(destination, source.Ptr + link->Offset, link->Length, batch.BufferTypeSize, UnsafeUtility.AlignOf<int>(), default, default);
                        //    }
                        //}

                    }

                    //if (batch.ComponentQueue.HasBuffer)
                    //{
                    //    // copy 
                    //    ArchetypeChunkBufferType<TempEntityBuffer> bufferType = entityEventBufferType;
                    //    UnsafeUtility.CopyStructureToPtr(ref batch.BufferTypeIndex, UnsafeUtility.AddressOf(ref componentType));

                    //    written = 0;
                    //    remaining = batch.ComponentQueue.CachedCount;
                    //    for (int j = 0; j < chunks.Length; j++)
                    //    {
                    //        var chunk = chunks[j];
                    //        var accessor = chunk.GetBufferAccessor(bufferType);

                    //        for (int x = 0; x < chunk.Count; x++)
                    //        {
                    //            DynamicBuffer<TempEntityBuffer> buffer = accessor[j];
                    //            var dstPtr = *(void**)&accessor;

                    //            var source = batch.ComponentQueue._bufferData;

                    //            for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
                    //            {
                    //                ref var b = ref source.GetBuffer(i);
                    //                if (b.Size == 0)
                    //                    continue;

                    //                var reader = b.AsReader();
                    //                reader.ReadNext<EventQueueBufferHeader>();

                    //                totalSize += buffer.Size;
                    //            }

                    //            buffer.EnsureCapacity();

                    //            batch.ComponentQueue.CopyComponentsTo(dstPtr, numToWrite * batch.ComponentTypeSize);

                    //            written++;
                    //            remaining--;
                    //        }


                    //        var slotsAvailable = chunk.Capacity - chunk.Count;
                    //        var offsetBytes = written * batch.BufferTypeSize; // ?? buffer size not element so * len?

                    //        var numToWrite = math.min(slotsAvailable, remaining);
                    //        batch.ComponentQueue.CopyComponentsTo(componentPtr + offsetBytes, numToWrite * batch.ComponentTypeSize);
                    //        remaining -= numToWrite;
                    //        written += numToWrite;
                    //    }
                    //}

                    batch.ComponentQueue.Clear();
                }

            }).Run();
        }

        public struct TempEntityBuffer : IBufferElementData
        {

        }

        /// <summary>
        /// Add an event directly to a default/main-thread EventQueue.
        /// </summary>
        public void Enqueue<T>(T item) where T : struct, IComponentData
        {
            GetQueue<T>().Enqueue(item);
        }

        //public unsafe void Enqueue<T,T2>(T item, T2* items, int length) where T : struct, IComponentData where T2 : unmanaged        
        //{
        //    var queue = GetQueue<T>();
        //    queue.Enqueue(item, items, length);
        //}

        public unsafe void Enqueue<T1, T2>(T1 item, T2* items, int length) 
            where T1 : struct, IComponentData 
            where T2 : unmanaged, IBufferElementData
        {
            var queue = GetQueue<T1,T2>();
            queue.Enqueue(item, items, length);
        }

        /// <summary>
        /// Acquire a the shared queue for creating events within jobs.
        /// </summary>
        public EventQueue<T> GetQueue<T>() where T : struct, IComponentData
        {
            return GetOrCreateBatch<T>().ComponentQueue.Cast<EventQueue<T>>();
        }

        public EventQueue<T1,T2> GetQueue<T1,T2>() 
            where T1 : struct, IComponentData
            where T2 : unmanaged, IBufferElementData
        {
            return GetOrCreateBatch<T1,T2>().ComponentQueue.Cast<EventQueue<T1,T2>>();
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

        private EventBatch GetOrCreateBatch<T1,T2>()
            where T1 : struct, IComponentData
            where T2 : unmanaged, IBufferElementData
        {
            var key = GetHashCode<T1, T2>();
            if (!_typeIndexToBatchMap.TryGetValue(key, out EventBatch batch))
            {
                batch = EventBatch.Create<T1,T2>(EntityManager, _eventComponent, Allocator.Persistent);
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

        //public EventQueue<TComponent, TBufferData> GetQueue<TComponent, TBufferData>()
        //    where TComponent : struct, IComponentData
        //    where TBufferData : struct, IBufferElementData
        //{
        //    return GetOrCreateBatch<TComponent, TBufferData>().ComponentQueue.Cast<TComponent, TBufferData>();
        //}

        //private EventBatch GetOrCreateBatch<TComponent, TBufferData>()
        //    where TComponent : struct, IComponentData
        //    where TBufferData : struct, IBufferElementData
        //{
        //    int typeIndex = TypeManager.GetTypeIndex<T>();
        //    if (!_typeIndexToBatchMap.TryGetValue(typeIndex, out EventBatch batch))
        //    {
        //        batch = EventBatch.Create<TComponent, TBuffer>(EntityManager, _eventComponent, Allocator.Persistent);
        //        _typeIndexToBatchMap[typeIndex] = batch;
        //    }
        //    return batch;
        //}

    }

}