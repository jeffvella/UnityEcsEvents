using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using System.Reflection;
using System;
using UnityEngine;
using Unity.Profiling;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Runtime.InteropServices;

namespace Vella.Events
{
    public struct EcsTestData : IComponentData //, IGetValue
    {
        public int value;

        public EcsTestData(int inValue)
        {
            value = inValue;
        }

        public override string ToString()
        {
            return value.ToString();
        }

        public int GetValue() => value;
    }

    public unsafe class EntityEventSystem : SystemBase
    {
        private UnsafeHashMap<int, int> _typeIndexToBatchMap;
        private ComponentType _eventComponent;
        private EntityQuery _allEventsQuery;
        private EntityQuery _allDisabledEventsQuery;
        private NativeList<EventArchetype> _batches;
        private NativeList<EventArchetype> _buffer;
        private UnsafeList<Entity> _entities;
        private UnsafeNativeArray _entitySlicer;
        private UnsafeList<ArchetypeChunk> _chunks;
        private UnsafeNativeArray _chunkSlicer;
        private UnsafeEntityManager _unsafeEntityManager;
        private EntityArchetype _disabledArchetype;
        private ArchetypeChunkComponentType<EntityEvent> _archetypeComponentTypeStub;
        private int _batchCount;
        private int _disabledTypeIndex;
        private UnsafeNativeArray _batchArray;

        protected override void OnCreate()
        {
            var typeCount = TypeManager.GetTypeCount();
            _typeIndexToBatchMap = new UnsafeHashMap<int, int>(typeCount, Allocator.Persistent); // possible with 2-type key that this hits max capacity, can it grow?

            _eventComponent = ComponentType.ReadOnly<EntityEvent>(); 

            _allEventsQuery = EntityManager.CreateEntityQuery(_eventComponent);

            _allDisabledEventsQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {
                    _eventComponent,
                    ComponentType.ReadOnly<Disabled>()
                }, Options = EntityQueryOptions.IncludeDisabled
            });

            _batches = new NativeList<EventArchetype>(typeCount, Allocator.Persistent); // doesn't need such high capacity, can grow.


            _buffer = new NativeList<EventArchetype>(typeCount, Allocator.Persistent);
            _entities = new UnsafeList<Entity>(typeCount, Allocator.Persistent);
            _entitySlicer = _entities.ToUnsafeNativeArray();
            _chunks = new UnsafeList<ArchetypeChunk>(0, Allocator.Persistent);
            _chunkSlicer = _entities.ToUnsafeNativeArray();
            _unsafeEntityManager = new UnsafeEntityManager(EntityManager);
            _disabledArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<EntityEvent>(), ComponentType.ReadWrite<Disabled>());

            _disabledTypeIndex = TypeManager.GetTypeIndex<Disabled>(); // move to field

            _batchArray = new UnsafeNativeArray();
            _batchArray.m_Safety = AtomicSafetyHandle.Create();
        }

        protected override void OnDestroy()
        {
            //var batchIndices = _typeIndexToBatchMap.GetValueArray(Allocator.Temp);
            for (int i = 0; i < _batches.Length; i++)
            {
                _batches[i].Dispose();
            }
            _batches.Dispose();

            _typeIndexToBatchMap.Dispose();
            //_batches.Dispose();
            _buffer.Dispose();
            _entities.Dispose();
            _chunks.Dispose();
        }

        public struct Markers
        {
            public static readonly ProfilerMarker Setup = new ProfilerMarker($"{nameof(Setup)}");
            public static readonly ProfilerMarker DestroyEntities = new ProfilerMarker($"{nameof(DestroyEntities)}");
            public static readonly ProfilerMarker CreateEntities = new ProfilerMarker($"{nameof(CreateEntities)}");
        }

        public struct Test
        {
            public int Value;
        }

        public struct EntityBatchInChunkProxy
        {
            public unsafe void* Chunk;
            public int StartIndex;
            public int Count;
        }

        protected override void OnUpdate()
        {

            //var countSW = Stopwatch.StartNew();
            // var mapCount = _typeIndexToBatchMap.Count();
            //countSW.Stop();
            //Debug.Log($"Count took {countSW.Elapsed.TotalMilliseconds:N4}");

            if (_batchCount == 0)
                return;

            //var chunks = _allEventsQuery.CreateArchetypeChunkArray(Allocator.TempJob);
            //_unsafeEntityManager.AddComponentToChunks(chunks.GetUnsafePtr(), chunks.Length, _disabledTypeIndex);

            if (_chunks.Length > 0)
            {
                _unsafeEntityManager.AddComponentToChunks(_chunks.Ptr, _chunks.Length, _disabledTypeIndex);
                _chunks.Clear();
            }

            var handle = GCHandle.Alloc(_batches, GCHandleType.Pinned);

            for (int i = 0; i < _batches.Length; i++)
            {
                ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(_batches.GetUnsafePtr(), i);

                var ptr1 = _batches.GetUnsafePtr();
                // tmp
                //batch.ComponentQueue.Clear();

                //batch.ComponentQueue.Enqueue(66);

                var requiredEntities = batch.ComponentQueue.ComponentCount();
                if (requiredEntities == 0)
                    continue;

                //var test = batch.ComponentQueue.GetBuffersForThread(-1);

                var cap = batch.Archetype.ChunkCapacity;

                var requiredFullChunks = requiredEntities / cap;
                var requiredPartialChunks = requiredEntities % cap == 0 ? 0 : 1;
                var requiredChunks = requiredFullChunks + requiredPartialChunks;

                // cases:
                // 1) no partials ->> a) re-use all chunks, b) re-use some chunks c) create all chunks.
                // 2) some partials ->> a) all from reused partials b) some from re-used partials c) create new partials. 

                //------------ // Full Chunk Conversion.

                // todo:
                // ** only crate chunks as a last resort, re-use existing/deactivated chunks.
                // 1) re-use full chunks and create leftover chunks + partials.

                var conversionFullChunks = math.min(requiredChunks, batch.FullChunks.Length);
                _unsafeEntityManager.RemoveComponentFromChunks(batch.FullChunks.Ptr, conversionFullChunks, _disabledTypeIndex); // over-creating by a whole chunk.

                // now some or all of batch.FullChunks need to go in the final output. 

                var remainingChunks = requiredChunks - conversionFullChunks;
                var remainingEntities = requiredEntities - (conversionFullChunks * cap); // possible negative remaining entities. overflow amount. 

                ///----------- // ** todo: on second cycle, when short full chunks, try consume partials before creating new chunks **

                if (remainingEntities > 0) // Create new chunks.
                {
                    // Create entities from partial inactive chunks.
                    if (batch.PartialChunks.Length > 0)
                    {
                        var conversionsRemaining = remainingEntities;
                        var entitiesConverted = 0;
                        var chunksDestroyed = 0;
                        var chunksCreated = 0;
                        var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.Temp);// chunk len capacity

                        for (int j = batch.PartialChunks.Length - 1; j != -1; j--)
                        {
                            var partial = batch.PartialChunks.Ptr[j];
                            var amountToMove = math.min(partial.Count, conversionsRemaining);

                            batchInChunks.Add(new EntityBatchInChunkProxy
                            {
                                Chunk = partial.GetChunkPtr(),
                                Count = amountToMove,
                                StartIndex = 0
                            });

                            if (amountToMove == partial.Count)
                                chunksDestroyed++;

                            chunksCreated++;
                            entitiesConverted += amountToMove;
                            conversionsRemaining -= amountToMove;

                            if (conversionsRemaining <= 0)
                                break;
                        }

                        _unsafeEntityManager.RemoveComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _disabledTypeIndex);
                        remainingEntities -= entitiesConverted;
                        var chunksConverted = entitiesConverted / cap + entitiesConverted % cap == 0 ? 0 : 1;
                        remainingChunks -= chunksConverted;

                        batch.PartialChunks.Length -= chunksDestroyed;

                        var idx = batch.ActiveChunks.Length - chunksCreated;
                        while (idx != batch.ActiveChunks.Length)
                            batch.PartialChunks.Add(batch.ActiveChunks[idx++]);
                    }
                  
                    // can we know in advance if partials will fill remaining?

                    if (remainingEntities > 0) // Create new chunks.
                    {

                        // Create entities from partial inactive chunks.
                        if (batch.PartialChunks.Length > 0)
                        {
                            var entitiesConverted = 0;
                            var chunksDestroyed = 0;
                            var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.Temp);

                            for (int j = batch.PartialChunks.Length; j != 0; j--)
                            {
                                var partial = batch.PartialChunks.Ptr[j];
                                batchInChunks.Add(new EntityBatchInChunkProxy
                                {
                                    Chunk = partial.GetChunkPtr(),
                                    Count = partial.Count,
                                    StartIndex = 0
                                });

                                entitiesConverted += partial.Count;
                                if (entitiesConverted >= remainingEntities)
                                    break;
                            }
                            _unsafeEntityManager.RemoveComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _disabledTypeIndex);
                            remainingEntities -= entitiesConverted;
                            remainingChunks -= entitiesConverted / cap;
                            batch.PartialChunks.Length -= chunksDestroyed; // all full chunks list should be full -check
                        }

                        // Ensure the batch can contain all the chunks we need.
                        batch.FullChunks.Resize(batch.FullChunks.Length + remainingChunks);

                        // Configure a resuable/temporary NativeArray because CreateChunk requires a NativeArray...
                        _batchArray.m_Buffer = (byte*)batch.FullChunks.Ptr + conversionFullChunks * sizeof(ArchetypeChunk);
                        _batchArray.m_Length = remainingChunks;

                        // Copy new chunks directly into the batch.
                        var ptr2 = _batches.GetUnsafePtr();
                        Debug.Assert(ptr1 == ptr2);

                        var tmp = _batches[i].Archetype;
                        EntityManager.CreateChunk(tmp, _batchArray.AsNativeArray<ArchetypeChunk>(), remainingEntities);

                        // Add this batch into the master list, so they can be efficiently cleared together on next frame.
                        //_chunks.AddRange(batch.FullChunks);

                        if (requiredPartialChunks > 0)
                        {
                            // Copy the last created into the partial group
                            batch.PartialChunks.Add(batch.FullChunks.Ptr[batch.FullChunks.Length - 1]);

                            // Bump the partial chunk off the end of the FullChunks list.
                            batch.FullChunks.Length--;
                        }
                    }
                }
                else // Split an activated full chunk into two partials by removing some.
                {
                    // Always a partial? 
                    // what happens if its exactly matched count to whats left in a chunk? 
                    // does it always take from a full chunk?

                    var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.Temp);
                    var partialIndex = conversionFullChunks - 1;
                    var partialChunk = batch.FullChunks.Ptr[partialIndex];

                    batchInChunks.Add(new EntityBatchInChunkProxy
                    {
                        Chunk = partialChunk.GetChunkPtr(),
                        Count = remainingEntities * -1,
                        StartIndex = remainingEntities + cap
                    });

                    _unsafeEntityManager.AddComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _disabledTypeIndex);

                    if (requiredPartialChunks > 0)
                    {
                        // A split of full chunk makes two partials
                        var inactivePartial = batch.InactiveChunks.Last();
                        batch.PartialChunks.Add(inactivePartial);
                        batch.PartialChunks.Add(partialChunk);

                        // Bump the partial chunk off the end of the FullChunks list.
                        batch.FullChunks.RemoveAtSwapBack(partialIndex);
                    }
                }

                int chunkCount = batch.FullChunks.Length + batch.PartialChunks.Length;
                _chunks.Resize(chunkCount);
                var size1 = batch.FullChunks.Length * sizeof(ArchetypeChunk);
                UnsafeUtility.MemCpy(_chunks.Ptr, batch.FullChunks.Ptr, size1);
                UnsafeUtility.MemCpy((byte*)_chunks.Ptr + size1, batch.PartialChunks.Ptr, batch.PartialChunks.Length * sizeof(ArchetypeChunk));

                //_chunks.AddRange(batch.FullChunks);// add active chunks from view isntead? some of these are unused.
                //_chunks.AddRange(batch.PartialChunks); 
                // or maybe split _chunks into two, re-used fulls can then avoid being switched archetype twice.


                //batch.ComponentQueue.Clear();
            }

            handle.Free();

            var batches = _batches;

            // Write component data.

            Job.WithCode(() =>
            {
                //int chunkOffset = 0;

                //ref var chunkSlicer = ref UnsafeUtilityEx.AsRef<UnsafeNativeArray>(chunkSlicerPtr);

                for (int i = 0; i < batches.Length; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(batches.GetUnsafePtr(), i);

                    var chunkCount = batch.Archetype.ChunkCount;

                    var chunks = new NativeArray<ArchetypeChunk>(chunkCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                    //var chunks = chunkSlicer.Slice<ArchetypeChunk>(chunkOffset, chunkCount);

                    //chunkOffset += chunkCount;

                    batch.Archetype.CopyChunksTo(chunks); // can be removed/simplified? write directly into the view?

                    MultiAppendBuffer.Reader components = batch.ComponentQueue.GetComponentReader();

                    for (int j = 0; j < chunkCount; j++)
                    {
                        var chunk = chunks[j];
                        var entityCount = chunk.Count;

                        components.CopyTo(batch.GetComponentPointer(chunk), entityCount * batch.ComponentTypeSize);
                    }

                    if (batch.HasBuffer)
                    {
                        MultiAppendBuffer.Reader links = batch.ComponentQueue.GetLinksReader();

                        for (int j = 0; j < chunkCount; j++)
                        {
                            ArchetypeChunk chunk = chunks[j];
                            var entityCount = chunk.Count;

                            byte* chunkBufferHeaders = batch.GetBufferPointer(chunk);
                            byte* chunkLinks = batch.GetBufferLinkPointer(chunk);

                            links.CopyTo(chunkLinks, entityCount * UnsafeUtility.SizeOf<BufferLink>());

                            for (int x = 0; x < entityCount; x++)
                            {
                                BufferHeaderProxy* bufferHeader = (BufferHeaderProxy*)(chunkBufferHeaders + x * batch.BufferTypeInfo.SizeInChunk);
                                BufferLink* link = (BufferLink*)(chunkLinks + x * UnsafeUtility.SizeOf<BufferLink>());

                                ref var source = ref batch.ComponentQueue._bufferData.GetBuffer(link->ThreadIndex);
                                BufferHeaderProxy.Assign(bufferHeader, source.Ptr + link->Offset, link->Length, batch.BufferTypeInfo.ElementSize, batch.BufferTypeInfo.AlignmentInBytes, default, default);
                            }
                        }
                    }

   
                    batch.ComponentQueue.Clear();
                }

            }).Run();

            //chunks.Dispose();
            //if (_chunks.Length != 0)
            //{

            //EntityManager.DestroyEntity(_allEventsQuery);
            //var sw = Stopwatch.StartNew();
            //EntityManager.DestroyEntity(_entitySlicer.AsNativeArray<Entity>());

            //var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.TempJob);
            //for (int i = 0; i < _buffer.Length; i++)
            //{
            //    var batch = _buffer[i]; // ref
            //    if (batch.PartialChunkIndex != -1)
            //    {
            //        var partialChunk = batch.Chunks.Ptr[batch.PartialChunkIndex];


            //        batchInChunks.Add(new EntityBatchInChunkProxy // store in batch. // need to replace array index rather than add.
            //        {
            //            Chunk = partialChunk.GetChunkPtr(),
            //            Count = partialChunk.Count,
            //            StartIndex = 0
            //        });

            //        _unsafeEntityManager.AddComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, disabledTypeIndex);
            //    }
            //}
            //batchInChunks.Dispose();

            // ** extra rogue chunks are being created here when they get switched from active to inactive.

            //_unsafeEntityManager.SetArchetype(_disabledArchetype, _entitySlicer.m_Buffer, _entities.Length);

            //sw.Stop();
            //Debug.Log($"Destroy took {sw.Elapsed.TotalMilliseconds:N4}");
            //}

            //EntityManager.UnlockChunk(_chunkSlicer.AsNativeArray<ArchetypeChunk>());

            //var destroySW = Stopwatch.StartNew();
            //if (_entities.Length != 0)
            //{
            //    if (_entities.Length < 1000)
            //    {

            //}
            //else
            //{

            //    EntityManager.DestroyEntity(_allEventsQuery);
            //    } 
            //}
            //destroySW.Stop();
            //Debug.Log($"Destroy took {destroySW.Elapsed.TotalMilliseconds:N4}");

            //var setupSW = Stopwatch.StartNew();

            //var batchCount = _batchCount;
            //var batchesToProcess = _buffer;
            //var mapPtr = UnsafeUtility.AddressOf(ref _typeIndexToBatchMap);
            //var batchesPtr = UnsafeUtility.AddressOf(ref _batches);
            //var entitiesPtr = UnsafeUtility.AddressOf(ref _entities);
            //var entitySlicerPtr = UnsafeUtility.AddressOf(ref _entitySlicer);
            //var chunksPtr = UnsafeUtility.AddressOf(ref _chunks);
            //var chunkSlicerPtr = UnsafeUtility.AddressOf(ref _chunkSlicer);


            //int totalRequiredEntities = 0;
            //int totalRequiredChunks = 0;

            //Job.WithCode(() =>
            //{
            //    ref var map = ref UnsafeUtilityEx.AsRef<UnsafeHashMap<int, EventArchetype>>(mapPtr);
            //    ref var batches = ref UnsafeUtilityEx.AsRef<NativeList<EventArchetype>>(batchesPtr);
            //    ref var entities = ref UnsafeUtilityEx.AsRef<UnsafeList<Entity>>(entitiesPtr);
            //    ref var entitySlicer = ref UnsafeUtilityEx.AsRef<UnsafeNativeArray>(entitySlicerPtr);
            //    ref var chunks1 = ref UnsafeUtilityEx.AsRef<UnsafeList<ArchetypeChunk>>(chunksPtr);
            //    ref var chunkSlicer = ref UnsafeUtilityEx.AsRef<UnsafeNativeArray>(chunkSlicerPtr);

            //    if (batches.Length < batchCount)
            //    {
            //        var values = map.GetValueArray(Allocator.Temp);
            //        batches.Clear();
            //        batches.AddRange(values.GetUnsafePtr(), values.Length);
            //    }

            //    batchesToProcess.Clear();
            //    var ptr = batches.GetUnsafePtr();

            //    for (int i = 0; i < batchCount; i++)
            //    {
            //        ref var archetype = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);

            //        var count = archetype.ComponentQueue.ComponentCount();
            //        if (count != 0)
            //        {
            //            batchesToProcess.Add(archetype);

            //            archetype.RequiredChunks = 1 + (count / archetype.Archetype.ChunkCapacity); // why is this not working on first pass?
            //            totalRequiredChunks += archetype.RequiredChunks;
            //            totalRequiredEntities += count;
            //        }
            //    }

            //    //if (chunks.Capacity < totalRequiredChunks)
            //    //{
            //    //    chunks.Resize(totalRequiredChunks);
            //        //chunkSlicer.m_Buffer = chunks.Ptr;
            //    //}
            //    //chunks.Length = totalRequiredChunks;
            //    //chunkSlicer.m_Length = totalRequiredChunks;
            //    //chunkSlicer.m_MaxIndex = totalRequiredChunks - 1;

            //    if (entities.Capacity < totalRequiredEntities)
            //    {
            //        entities.Resize(totalRequiredEntities);
            //        entitySlicer.m_Buffer = entities.Ptr;
            //    }
            //    entities.Length = totalRequiredEntities;
            //    entitySlicer.m_Length = totalRequiredEntities;
            //    entitySlicer.m_MaxIndex = totalRequiredEntities - 1;

            //}).Run();

            //_chunks.Resize(totalRequiredChunks);

            //Job.WithCode(() =>
            //{


            //});
            // **** if events exist every frame, then don't do anything with them except set new data. / remove excess / create more ****

            //var requiredEntities = count;

            //var chunkCapacity = batch.Archetype.ChunkCapacity; // both archetypes are the same capacity because of zero sized disabled component.

            //var requiredChunksCount = 1 + (requiredEntities / chunkCapacity);

            //if (requiredEntities == 0)
            //    continue;

            //var availableChunkCount = batch.Chunks.Length;

            //// must always have at least +1 inactive full chunk than needed, 
            //// otherwise we'd have to keep a sum of entities available in partial chunks.
            ////const int backupChunks = 1;

            //// resize early so that it can be done in burst.
            ////if (batch.RequiredChunks > currentChunkCount)
            ////{
            ////    batch.Chunks.Resize(batch.RequiredChunks);
            ////}

            ////var mustCreateNewChunks = batch.RequiredChunks - fullChunksCount; 

            //var partialChunksCount = batch.FirstFullChunkIndex;
            //var fullChunksCount = availableChunkCount - partialChunksCount;

            //var newActiveChunksRequired = requiredChunksCount - fullChunksCount; // 10 required - 30 available = -20  = create no chunks
            //// 30 required, 10 available = 20 create

            //if (newActiveChunksRequired > 0) 
            //{
            //var chunks1 = _allDisabledEventsQuery.CreateArchetypeChunkArray(Allocator.TempJob);
            //for (int i = 0; i < chunks1.Length; i++)
            //{
            //    var chunk = chunks1[i];

            //}
            //_unsafeEntityManager.AddComponentToChunks(chunks1.GetUnsafePtr(), chunks1.Length, disabledTypeIndex);
            //chunks1.Dispose();

            // Activate full chunks.

            //if (fullChunksCount > 0)
            //{
            //    var fullChunksPtr = (ArchetypeChunk*)((byte*)batch.Chunks.Ptr + partialChunksCount * sizeof(ArchetypeChunk));
            //    _unsafeEntityManager.RemoveComponentFromChunks(fullChunksPtr, fullChunksCount, disabledTypeIndex);
            //}

            //batch.Chunks.Capacity = availableChunkCount + newActiveChunksRequired + backupChunks; // required chunks count
            //batch.Chunks.Clear();
            //batch.Chunks.Capacity = requiredChunksCount;
            // Create Actives
            //var newActiveEntityCount = requiredEntities - fullChunksCount * chunkCapacity;
            //var newActiveChunks = new NativeArray<ArchetypeChunk>(newActiveChunksRequired, Allocator.Temp);

            //var newActiveEntityCount = requiredEntities - fullChunksCount * chunkCapacity;

            //var newActiveChunks = new NativeArray<ArchetypeChunk>(requiredChunksCount, Allocator.Temp);
            //EntityManager.CreateChunk(batch.Archetype, newActiveChunks, requiredEntities); // write straight into batch?

            //batch.Chunks.AddRangeNoResize(newActiveChunks.GetUnsafePtr(), requiredChunksCount);

            //// Creating chunks will usually make a partial chunk at the end.
            //if (newActiveChunksRequired == 1 || (newActiveEntityCount % newActiveChunksRequired) != 0) // if doesn't divide exactly. //newActiveChunksRequired == 1||  newEntities % newActiveChunksRequired
            //{
            //    // move partial to the front and update tracking of  where full chunks start
            //    batch.Chunks.Swap(batch.FirstFullChunkIndex++, batch.Chunks.Length - 1); // sans backupChunks
            //}

            //// Create inactives // ** only required if a partial chunk is created? also needs to be swapped to start? 
            //// maybe not because next frame will reset them all to inactive archetype.
            //var newInactiveEntityCount = chunkCapacity * backupChunks;
            //var newInactiveChunks = new NativeArray<ArchetypeChunk>(backupChunks, Allocator.Temp, NativeArrayOptions.ClearMemory);
            //EntityManager.CreateChunk(batch.InactiveArchetype, newInactiveChunks, newInactiveEntityCount); // write straight into batch?
            //batch.Chunks.AddRangeNoResize(newInactiveChunks.GetUnsafePtr(), newInactiveChunks.Length);

            //newInactiveChunks.Dispose();
            //newActiveChunks.Dispose();

            // swapback to keep partials at the front or create only full chunks?
            // full chunks would keep fewer partials which might speed up the most common case of full-reuse.
            //  }
            //else
            //{
            //    // ?>? easier to do full chunks and then batch add disabled?
            //    // Activate full chunks.

            //    // this needs to be offset -1 so that entities are created up to and excluding the last partial chunk.

            //    var requiredFullChunkConversions = requiredChunksCount - 1; 
            //    if (requiredFullChunkConversions > 0) 
            //    {
            //        // ptr needs to be offset +X so that in the case of partial chunks being consumed below, 
            //        // and rolling into the next full chunk, that full chunk needs to an 'inactive' chunk.
            //        // otherwise RemoveComponentEntitiesBatch() will fail because it finds two of the same archetype.
            //        // note: for this to work there always needs to be +1 inactive chunk.
            //        //** double check that there are enough chunks for the +inactiveChunksToCreate

            //        // !! must swap back the backstop chunk.

            //        //var fullChunksPtr = (ArchetypeChunk*)((byte*)batch.Chunks.Ptr + (partialChunksCount + inactiveChunksToCreate) * sizeof(ArchetypeChunk));
            //        var fullChunksPtr = (ArchetypeChunk*)((byte*)batch.Chunks.Ptr + partialChunksCount * sizeof(ArchetypeChunk));
            //        _unsafeEntityManager.RemoveComponentFromChunks(fullChunksPtr, requiredFullChunkConversions, disabledTypeIndex); 
            //    }

            //    var batchInChunks = new NativeList<EntityBatchInChunkProxy>(availableChunkCount, Allocator.Temp);
            //    var remaining = requiredEntities - requiredFullChunkConversions * chunkCapacity;

            //    while (remaining > 0) 
            //    {
            //        // partial chunks are consumed first because they're sorted to the front.
            //        var giver = batch.Chunks.Ptr[0];
            //        if (giver.Full) 
            //        {
            //            // this full chunk is now going to be a partial
            //            batch.FirstFullChunkIndex++;
            //        }

            //        Debug.Assert(remaining <= giver.Capacity);

            //        var numToMove = math.min(remaining, giver.Count);
            //        if (numToMove == giver.Count)
            //        {
            //            // giver is about to be destroyed, discard it
            //            // ** suspect that this chunk is brought back when the next tick flips 
            //            // the archectype back to inactive and it becomes a new partial again. 
            //            // in which case it should be left at the begining? 
            //            // it somehow needs to get back to the start.
            //            // a collection view could be shifted back/forward to exclude them while invalid?
            //            // if they're left in _chunks collection then AddComponentToChunks will blow up reverting it next frame.
            //            //batch.Chunks.RemoveAtSwapBack(0); // * swaps back the next full from end? this will blow up
            //            //batch.Chunks.Length--;
            //            //batch.FirstFullChunkIndex--;
            //        }

            //        // Define a the region of entities to be moved from the chunk.
            //        batchInChunks.Add(new EntityBatchInChunkProxy
            //        {
            //            Chunk = giver.GetChunkPtr(),
            //            Count = numToMove,
            //            StartIndex = 0 // take them from the end should reduce swap back time no?
            //        }); // possible to get negative values for count if count > amount in specified chunk.

            //        remaining -= numToMove;
            //    }
            //    _unsafeEntityManager.RemoveComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, disabledTypeIndex);

            //    batchInChunks.Dispose();
            //    //var numEntitiesToExclude = chunkCapacity - (requiredEntities - ((batch.RequiredChunks - 1) * chunkCapacity));
            //    //var numToMove = chunkCapacity - numEntitiesToExclude;








            //    //    remaining -= amount;
            //    //}
            //    // check for destroyed and swap back because its gonna sit there at [0] and mess up the next cycle.


            //    // In order for the master list of chunks to be linear for active followed by inactive chunks, 
            //    // the partials need to be swapped.
            //    //var lastIndex = batch.Chunks.Length - 1;
            //    //var partialActive = batch.Archetype.FirstChunk(); // this is useless here because it doesnt return the result of above RemoveComponentEntitiesBatch


            //    // Its possible there are additional full chunks that are still flagged as disabled, 
            //    // but these should be grouped towards the end of the collection.
            //    // todo: consider not writing this information to batch if batches are to processed by different threads.


            //}



            // ** instead of having split collections for partial and full chunks, just store an index to the partial chunk
            // then no management is required to reorganize them after deactivation, the archetypes simply change in place.
            // ** then we assume all chunks are reset after removing the disabled component.

            //var fullChunkCount = requiredChunks - 1;
            //batch.ActiveFullChunks = fullChunkCount;

            //batch.ActiveEntityCount = requiredEntities;


            //chunksUsed += requiredChunks;

            // consider -  store together wrapped in meta container (to include full/partial status inline) to reduce the number of writes?
            // or consolidate lists at destroy time? or fire multiple destroy calls one per batch?


            //_chunks.Resize(requiredChunks);
            //var size = requiredChunks * sizeof(ArchetypeChunk);
            //UnsafeUtility.MemCpy(_chunks.Ptr, batch.Chunks.Ptr, size);
            //chunkOffsetBytes += size;

            //((UnsafeList*)UnsafeUtility.AddressOf(ref _chunks))->AddRange<ArchetypeChunk>(batch.Chunks.Ptr, fullChunkCount);
            //((UnsafeList*)UnsafeUtility.AddressOf(ref _chunks))->AddRange<ArchetypeChunk>(batch.PartialChunks.Ptr, 1);

            // ** now we have all the entities we need created. ** 

            //var sum = 0;
            //for (int x = 0; x < numFullChunksToReactivate; x++)
            //{
            //    var chunk = batch.PartialChunks.Ptr[x];
            //    if (!chunk.Full)
            //    {
            //        // use batchInChunk to create entities.

            //        //while (srcRemainingCount > 0)
            //        //{
            //        //    Chunk* dstChunk = GetChunkWithEmptySlots(ref archetypeChunkFilter);
            //        //    int dstCount = Move(new EntityBatchInChunk
            //        //    {
            //        //        Chunk = srcChunk,
            //        //        Count = srcRemainingCount,
            //        //        StartIndex = startIndex
            //        //    }, dstChunk);
            //        //    srcRemainingCount -= dstCount;
            //        //}

            //        var freeSpaceInChunk = chunk.Capacity - chunk.Count;

            //        batchInChunks.Ptr[batchInChunks.Length++] = new EntityBatchInChunkProxy
            //        {
            //            Chunk = chunk.GetChunkPtr(),
            //            Count = freeSpaceInChunk,
            //            StartIndex = chunk.Count - 1
            //        };

            //        //var freeSpaceInChunk = chunk.Capacity - chunk.Count;
            //        //_unsafeEntityManager.CreateEntity(batch.Archetype, _entities.Ptr, entityCount, sum);
            //    }
            //    else
            //    {
            //        // re-use a full chunk without creating anything.
            //    }
            //    sum += chunk.Count;
            //}

            //// If you remove all the entities this way form a component 
            //// * it will destroy the chunk too.
            //// * it can create new chunks in the target archetype as needed.
            //_unsafeEntityManager.RemoveComponentEntitiesBatch((UnsafeList*)&batchInChunks, disabledTypeIndex);

            // If full use RemoveComponentFromChunks and if partial use RemoveComponentEntitiesBatch.
            // to re-use previously used&disabled entities.
            // then create new entities for whatever is left over.

            //var availableEntities = 

            // repurpose existing chunks by removing the disabled component
            // the entities in the chunk can be immediately re-used
            // entities need to be created for the remainder. (which may or may not be placed in the same chunk?)

            //_unsafeEntityManager.CreateEntity(batch.Archetype, _entities.Ptr, entityCount, created);

            //created += requiredEntities;
            // }


            // todo - replace with something more efficient.
            //var chunks = _allEventsQuery.CreateArchetypeChunkArray(Allocator.TempJob);
            //_chunks.Clear();
            //_chunks.Resize(chunks.Length);
            //var size = chunks.Length * sizeof(ArchetypeChunk);
            //UnsafeUtility.MemCpy(_chunks.Ptr, chunks.GetUnsafePtr(), size);
            //chunks.Dispose();

            //return;


            //Job.WithCode(() =>
            //{
            //    var ptr = batchesToProcess.GetUnsafePtr();
            //    int chunkOffset = 0;

            //    ref var chunkSlicer = ref UnsafeUtilityEx.AsRef<UnsafeNativeArray>(chunkSlicerPtr);

            //    for (int i = 0; i < batchesToProcess.Length; i++)
            //    {
            //        ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);

            //        var chunkCount = batch.Archetype.ChunkCount;

            //        //var chunks = new NativeArray<ArchetypeChunk>(chunkCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            //        var chunks = chunkSlicer.Slice<ArchetypeChunk>(chunkOffset, chunkCount);

            //        chunkOffset += chunkCount;


            //        // * get only enough chunks to contain the entities we need to copy
            //        batch.Archetype.CopyChunksTo(chunks);

            //        MultiAppendBuffer.Reader components = batch.ComponentQueue.GetComponentReader();

            //        for (int j = 0; j < chunkCount; j++)
            //        {
            //            var chunk = chunks[j];
            //            var entityCount = chunk.Count;

            //            components.CopyTo(batch.GetComponentPointer(chunk), entityCount * batch.ComponentTypeSize);
            //        }

            //        if (batch.HasBuffer)
            //        {
            //            MultiAppendBuffer.Reader links = batch.ComponentQueue.GetLinksReader();

            //            for (int j = 0; j < chunkCount; j++)
            //            {
            //                ArchetypeChunk chunk = chunks[j];
            //                var entityCount = chunk.Count;

            //                byte* chunkBufferHeaders = batch.GetBufferPointer(chunk);
            //                byte* chunkLinks = batch.GetBufferLinkPointer(chunk);

            //                links.CopyTo(chunkLinks, entityCount * UnsafeUtility.SizeOf<BufferLink>());

            //                for (int x = 0; x < entityCount; x++)
            //                {
            //                    BufferHeaderProxy* bufferHeader = (BufferHeaderProxy*)(chunkBufferHeaders + x * batch.BufferTypeInfo.SizeInChunk);
            //                    BufferLink* link = (BufferLink*)(chunkLinks + x * UnsafeUtility.SizeOf<BufferLink>());

            //                    ref var source = ref batch.ComponentQueue._bufferData.GetBuffer(link->ThreadIndex);
            //                    BufferHeaderProxy.Assign(bufferHeader, source.Ptr + link->Offset, link->Length, batch.BufferTypeInfo.ElementSize, batch.BufferTypeInfo.AlignmentInBytes, default, default);
            //                }
            //            }
            //        }

            //        //chunks.Dispose();

            //        batch.ComponentQueue.Clear();
            //    }

            //}).Run();

            //EntityManager.LockChunk(_chunkSlicer.AsNativeArray<ArchetypeChunk>());

            //setSW.Stop();
            //Debug.Log($"SetData took {setSW.Elapsed.TotalMilliseconds:N4}");
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
            if (!_typeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = EventArchetype.Create<T>(EntityManager, _eventComponent, Allocator.Persistent);
                index = _batchCount++;
                _batches.ResizeUninitialized(_batchCount);
                _batches[index] = batch;
                _typeIndexToBatchMap[key] = index;
                return batch;
            }
            return _batches[index];
        }

        public EventArchetype GetOrCreateBatch(TypeManager.TypeInfo typeInfo)
        {
            int key = typeInfo.TypeIndex;
            if (!_typeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = EventArchetype.Create(EntityManager, _eventComponent, typeInfo, Allocator.Persistent);
                index = _batchCount++;
                _batches.ResizeUninitialized(_batchCount);
                _batches[index] = batch;
                _typeIndexToBatchMap[key] = index;
                return batch;
            }
            return _batches[index];
            //int key = typeInfo.TypeIndex;
            //if (!_typeIndexToBatchMap.TryGetValue(key, out EventArchetype batch))
            //{
            //    batch = EventArchetype.Create(EntityManager, _eventComponent, typeInfo, Allocator.Persistent);
            //    _typeIndexToBatchMap[key] = batch;
            //    _batchCount++;
            //}
            //return batch;
        }

        private EventArchetype GetOrCreateBatch<TComponent,TBufferData>()
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            //var key = GetHashCode<TComponent, TBufferData>();
            //if (!_typeIndexToBatchMap.TryGetValue(key, out EventArchetype batch))
            //{
            //    batch = EventArchetype.Create<TComponent,TBufferData>(EntityManager, _eventComponent, Allocator.Persistent);
            //    _typeIndexToBatchMap[key] = batch;
            //    _batchCount++;
            //}
            //return batch;
            int key = GetHashCode<TComponent, TBufferData>();
            if (!_typeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = EventArchetype.Create<TComponent, TBufferData>(EntityManager, _eventComponent, Allocator.Persistent);
                index = _batchCount++;
                _batches.ResizeUninitialized(_batchCount);
                _batches[index] = batch;
                _typeIndexToBatchMap[key] = index;
                return batch;
            }
            return _batches[index];
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
