using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

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
            var entityEventComponentType = GetArchetypeChunkComponentType<EntityEvent>();
            var batchesToProcess = _buffer;

            // * Ensure we're working from the most current list of batches.
            // * Filter in order to only process batches with events queued.

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
                    if (batch.Queue.Count() != 0)
                    {
                        batchesToProcess.Add(batch);
                    }
                }

            }).Run();

            // * Generate all events in bulk at once for each event type. 

            for (int i = 0; i < _buffer.Length; i++)
            {
                EntityManager.CreateEntity(_buffer[i].Archetype, _buffer[i].Queue.CachedCount, Allocator.Temp);
            }

            // * Block copy component data into chunks.

            Job.WithCode(() =>
            {
                var ptr = batchesToProcess.GetUnsafePtr();

                for (int i = 0; i < batchesToProcess.Length; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventBatch>(ptr, i);

                    // Get all the chunks for this archetype
                    var chunks = new NativeArray<ArchetypeChunk>(batch.Archetype.ChunkCount, Allocator.Temp);
                    batch.Archetype.CopyChunksTo(chunks);

                    // Make an ArchetypeComponentType by TypeIndex
                    ArchetypeChunkComponentType<EntityEvent> componentType = entityEventComponentType;
                    UnsafeUtility.CopyStructureToPtr(ref batch.TypeIndex, UnsafeUtility.AddressOf(ref componentType));

                    // Copy entities into chunks
                    var written = 0;
                    var remaining = batch.Queue.CachedCount;
                    for (int j = 0; j < chunks.Length; j++)
                    {
                        var chunk = chunks[j];
                        var componentPtr = (byte*)chunk.GetNativeArray(componentType).GetUnsafeReadOnlyPtr();
                        var slotsAvailable = chunk.Capacity - chunk.Count;
                        var offsetBytes = written * batch.ComponentSize;
                        var numToWrite = math.min(slotsAvailable, remaining);
                        batch.Queue.CopyEventsTo(componentPtr + offsetBytes, numToWrite * batch.ComponentSize);
                        remaining -= numToWrite;
                        written += numToWrite;
                    }

                    batch.Queue.Clear();
                }

            }).Run();
        }

        /// <summary>
        /// Add an event directly to a default/main-thread EventQueue.
        /// </summary>
        public void Enqueue<T>(T item) where T : struct, IComponentData
        {
            GetQueue<T>().Enqueue(item);
        }

        /// <summary>
        /// Acquire a the shared queue for creating events within jobs.
        /// </summary>
        public EventQueue<T> GetQueue<T>() where T : struct, IComponentData
        {
            return GetOrCreateBatch<T>().Queue.Cast<T>();
        }

        private EventBatch GetOrCreateBatch<T>() where T : struct, IComponentData
        {
            int typeIndex = TypeManager.GetTypeIndex<T>();
            if (!_typeIndexToBatchMap.TryGetValue(typeIndex, out EventBatch batch))
            {
                batch = EventBatch.Create<T>(EntityManager, _eventComponent, Allocator.Persistent);
                _typeIndexToBatchMap[typeIndex] = batch;
            }
            return batch;
        }

    }

}