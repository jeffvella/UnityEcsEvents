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
using System.Linq;

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
        private void* _dataPtr;

        private ref EventSystemData _data => ref UnsafeUtilityEx.AsRef<EventSystemData>(_dataPtr);

        public EntityQuery _allEventsQuery;

        public EntityQuery _allDisabledEventsQuery;

        /// <summary>
        /// System fields are stored here so that they're pinned.
        /// </summary>
        public struct EventSystemData
        {
            public UnsafeHashMap<int, int> TypeIndexToBatchMap;
            public ComponentType EventComponent;
            public NativeList<EventArchetype> Batches;
            public NativeList<EventArchetype> _buffer;
            public UnsafeList<Entity> _entities;
            public UnsafeNativeArray _entitySlicer;
            public UnsafeList<ArchetypeChunk> _chunks;
            public UnsafeNativeArray _chunkSlicer;
            public UnsafeEntityManager _unsafeEntityManager;
            public EntityArchetype _disabledArchetype;
            public ArchetypeChunkComponentType<EntityEvent> _archetypeComponentTypeStub;
            public int _batchCount;
            public int _disabledTypeIndex;
            public UnsafeNativeArray _batchArray;
        }

        protected override void OnCreate()
        {


            EventSystemData data = default;

            const int StartingBatchCount = 10;

            data.TypeIndexToBatchMap = new UnsafeHashMap<int, int>(StartingBatchCount, Allocator.Persistent);
            data.EventComponent = ComponentType.ReadOnly<EntityEvent>();

            _allEventsQuery = EntityManager.CreateEntityQuery(data.EventComponent);
            _allDisabledEventsQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] {
                    data.EventComponent,
                    ComponentType.ReadOnly<Disabled>()
                }, Options = EntityQueryOptions.IncludeDisabled
            });

            data.Batches = new NativeList<EventArchetype>(StartingBatchCount, Allocator.Persistent);
            data._buffer = new NativeList<EventArchetype>(StartingBatchCount, Allocator.Persistent);
            data._entities = new UnsafeList<Entity>(StartingBatchCount, Allocator.Persistent);
            data._entitySlicer = data._entities.ToUnsafeNativeArray();
            data._chunks = new UnsafeList<ArchetypeChunk>(0, Allocator.Persistent);
            data._chunkSlicer = data._entities.ToUnsafeNativeArray();
            data._unsafeEntityManager = new UnsafeEntityManager(EntityManager);
            data._disabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();
            data._batchArray = new UnsafeNativeArray();
            data._batchArray.m_Safety = AtomicSafetyHandle.Create();
            data._disabledArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<EntityEvent>(), 
                ComponentType.ReadWrite<Disabled>());

            _dataPtr = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<EventSystemData>(), 
                UnsafeUtility.AlignOf<EventSystemData>(), Allocator.Persistent);
            UnsafeUtility.CopyStructureToPtr(ref data, _dataPtr);
        }

        protected override void OnDestroy()
        {
            for (int i = 0; i < _data.Batches.Length; i++)
            {
                _data.Batches[i].Dispose();
            }
            _data.Batches.Dispose();
            _data.TypeIndexToBatchMap.Dispose();
            _data._buffer.Dispose();
            _data._entities.Dispose();
            _data._chunks.Dispose();

            UnsafeUtility.Free(_dataPtr, Allocator.Persistent);
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

        public struct RemoveComponentFromChunksOperation
        {
            public unsafe void* Chunks;
            public int Count;
            public int TypeIndex;
        }

        public struct RemoveComponentEntitiesBatch
        {
            public UnsafeList* Batches;
            public int TypeIndex;
        }

        public struct AddComponentEntitiesBatch
        {
            public UnsafeList* Batches;
            public int TypeIndex;
        }

        public struct CreateChunksOperation
        {
            public EntityArchetype Archetype;
            public void* ChunksOut;
            public int EntityCount;
        }

        protected override void OnUpdate()
        {
            if (_data._batchCount == 0)
                return;

            //var sw1 = Stopwatch.StartNew();

            //var countSW = Stopwatch.StartNew();
            if (_data._chunks.Length > 0)
            {
                for (int i = 0; i < _data._chunks.Length; i++)
                {
                    _data._chunks.Ptr[i].Invalid(); // triggers null reference exception rather than editor crash.
                }

                // todo, exclude batches that will need the events again from last frame, we can just set data on them without any other processing.
                _data._unsafeEntityManager.AddComponentToChunks(_data._chunks.Ptr, _data._chunks.Length, _data._disabledTypeIndex);
                _data._chunks.Clear();
            }
            //countSW.Stop();
            //Debug.Log($"Destroy took {countSW.Elapsed.TotalMilliseconds:N4}");

            //var countSW1 = Stopwatch.StartNew();

            //var handle = GCHandle.Alloc(_data._batches, GCHandleType.Pinned);
            //var handle2 = GCHandle.Alloc(_data._chunks, GCHandleType.Pinned);  

            var ptr = _data.Batches.GetUnsafePtr();
            for (int i = 0; i < _data.Batches.Length; i++)
            {
                ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);
                var requiredEntities = batch.ComponentQueue.ComponentCount();
                if (requiredEntities == 0)
                    continue;

                var cap = batch.Archetype.ChunkCapacity;
                var requiredFullChunks = requiredEntities / cap;
                var requiredPartialChunks = requiredEntities % cap == 0 ? 0 : 1;
                var requiredChunks = requiredFullChunks + requiredPartialChunks;
                var conversionFullChunks = math.min(requiredChunks, batch.FullChunks.Length);

                // Create entities by removing the disabled component from otherwise identical full chunks.
                _data._unsafeEntityManager.RemoveComponentFromChunks(batch.FullChunks.Ptr, conversionFullChunks, _data._disabledTypeIndex);

                var remainingChunks = requiredChunks - conversionFullChunks;
                var remainingEntities = requiredEntities - (conversionFullChunks * cap); 

                if (remainingEntities > 0)
                {
                    // Create entities from partial inactive chunks.
                    // todo: evaluate if 1 partial only ever exists, if so, this can be simplified considerably.
                    if (batch.PartialChunks.Length > 0)
                    {
                        var conversionsRemaining = remainingEntities;
                        var entitiesConverted = 0;
                        var chunksDestroyed = 0;
                        var chunksCreated = 0;
                        var batchInChunks = new NativeList<EntityBatchInChunkProxy>(batch.PartialChunks.Length, Allocator.Temp);

                        for (int j = batch.PartialChunks.Length - 1; j != -1; j--)
                        {
                            var partial = batch.PartialChunks.Ptr[j];

                            //Debug.Assert(!partial.Invalid());

                            var amountToMove = math.min(partial.Count, conversionsRemaining);

                            batchInChunks.Add(new EntityBatchInChunkProxy // todo: try to store these up for all _batches and do them once together at the end.
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

                        _data._unsafeEntityManager.RemoveComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _data._disabledTypeIndex);
                        remainingEntities -= entitiesConverted;
                        var chunksConverted = entitiesConverted / cap + entitiesConverted % cap == 0 ? 0 : 1; 
                        remainingChunks -= chunksConverted;

                        batch.PartialChunks.Length -= chunksDestroyed;

                        var idx = batch.ActiveChunks.Length - chunksCreated;
                        while (idx != batch.ActiveChunks.Length) // possibly because active chunks length = 1, so it started -2
                            batch.PartialChunks.Add(batch.ActiveChunks[idx++]); // this is wrong, added 2x destroyed chunks to partials.
                    }
                  
                    // todo - evaluate if there is ever a case were partials need to be consumed (above) and then more chunks created (below) and simplify if false.
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

                            // todo: see if there's a way to do all of this building work in burst, store it out into an instruction collection
                            // and then loop through the removes outside burst afterwards, or find a way to call the burst ptrs from within burst / FuncitonPointer<T>
                            _data._unsafeEntityManager.RemoveComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _data._disabledTypeIndex);
                            remainingEntities -= entitiesConverted;
                            remainingChunks -= entitiesConverted / cap;
                            batch.PartialChunks.Length -= chunksDestroyed; // all full chunks list should be full -check
                        }

                        // Ensure the batch can contain all the chunks we need.
                        batch.FullChunks.Resize(batch.FullChunks.Length + remainingChunks);

                        // Configure a resuable/temporary NativeArray because CreateChunk requires a NativeArray...
                        _data._batchArray.m_Buffer = (byte*)batch.FullChunks.Ptr + conversionFullChunks * sizeof(ArchetypeChunk);
                        _data._batchArray.m_Length = remainingChunks;

                        // Copy new chunks directly into the batch.
                        EntityManager.CreateChunk(_data.Batches[i].Archetype, _data._batchArray.AsNativeArray<ArchetypeChunk>(), remainingEntities);

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
                    var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.Temp);
                    var partialIndex = conversionFullChunks - 1;
                    var activePartial = batch.FullChunks.Ptr[partialIndex];
                    var inactiveCount = batch.InactiveChunks.Length;

                    batchInChunks.Add(new EntityBatchInChunkProxy
                    {
                        Chunk = activePartial.GetChunkPtr(),
                        Count = remainingEntities * -1,
                        StartIndex = remainingEntities + cap
                    });

                    // todo, can this be processed for all batches later and the builder logic moved into burst?
                    _data._unsafeEntityManager.AddComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _data._disabledTypeIndex);

                    if (requiredPartialChunks > 0)
                    {
                        // A split of full chunk makes two partials

                        //if (inactivePartial.Full) // a new is only created if there isnt viable space in an existing chunk.
                        //{
                        //    Debug.Log("Full after split - Added to existing chunk");
                        //}
                        //if (inactiveCount == batch.InactiveChunks.Length)
                        //{
                        //    Debug.Log("Same counts after split - Added to existing chunk");
                        //}
                        var inactivePartial = batch.InactiveChunks.Last(); // todo: ??? scan back through to find the last partial and/or check .Full
                        if (inactiveCount == batch.InactiveChunks.Length) // a new chunk is not always created.
                        {
                            // this means that another chunk in the partial collection might be now full.
                            batch.PartialChunks.Add(inactivePartial); // getting multiple chunks that are full in PartialChunks.
                        }

                        // Bump the partial chunk off the end of the FullChunks list.
                        batch.PartialChunks.Add(activePartial);
                        batch.FullChunks.RemoveAtSwapBack(partialIndex);
                    }
                }

                // todo: is it necessary or useful to include all chunks, even if they're not used or can this just copy from the active view.
                //var offset = _chunks.Length * sizeof(ArchetypeChunk);
                //int chunkCount = _chunks.Length + batch.FullChunks.Length + batch.PartialChunks.Length;
                //_chunks.Resize(chunkCount);
                //var size1 = batch.FullChunks.Length * sizeof(ArchetypeChunk);
                //var size2 = batch.PartialChunks.Length * sizeof(ArchetypeChunk);
                //var startPtr = (byte*)_chunks.Ptr + offset;
                //UnsafeUtility.MemCpy(startPtr, batch.FullChunks.Ptr, size1);
                //UnsafeUtility.MemCpy(startPtr + size1, batch.PartialChunks.Ptr, size2);

                //todo need a test to make sure multiple batches are dumping into the combined _chunks list.

                foreach (var chunk in batch.ActiveChunks)
                    chunk.Invalid();
                foreach (var chunk in batch.InactiveChunks)
                    chunk.Invalid();
                //for (int x = 0; x < _chunks.Length; x++)
                //   _chunks.Ptr[x].Invalid();

                //var test = new NativeArray<ArchetypeChunk>(batch.ActiveChunks.Length, Allocator.Temp);
                //batch.ActiveChunks.CopyTo(test.GetUnsafePtr());

                batch.ActiveChunks.AddTo(ref _data._chunks);

                //_chunks.AddRange(batch.FullChunks);// add active chunks from view isntead? some of these are unused.
                //_chunks.AddRange(batch.PartialChunks);
                // or maybe split _chunks into two, re-used fulls can then avoid being switched archetype twice.
                //batch.ComponentQueue.Clear();
            }

            //countSW1.Stop();
            //Debug.Log($"Create Entities took {countSW1.Elapsed.TotalMilliseconds:N4}");

            //var countSW2 = Stopwatch.StartNew();

            var batches = _data.Batches;
            Job.WithCode(() =>
            {
                for (int i = 0; i < batches.Length; i++)
                {
                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(batches.GetUnsafePtr(), i);

                    var chunkCount = batch.Archetype.ChunkCount;

                    // todo: avoid allocation?
                    var chunks = new NativeArray<ArchetypeChunk>(chunkCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                    // todo can be removed/simplified? write directly into the view?
                    batch.Archetype.CopyChunksTo(chunks); 

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



            //sw1.Stop();
            //Debug.Log($"Update took {sw1.Elapsed.TotalMilliseconds:N4}");

            //countSW2.Stop();
            //Debug.Log($"Set Data took {countSW1.Elapsed.TotalMilliseconds:N4}");

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
            if (!_data.TypeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = EventArchetype.Create<T>(EntityManager, _data.EventComponent, Allocator.Persistent);
                index = _data._batchCount++;
                _data.Batches.ResizeUninitialized(_data._batchCount);
                _data.Batches[index] = batch;
                _data.TypeIndexToBatchMap[key] = index;
                return batch;
            }
            return _data.Batches[index];
        }

        public EventArchetype GetOrCreateBatch(TypeManager.TypeInfo typeInfo)
        {
            int key = typeInfo.TypeIndex;
            if (!_data.TypeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = EventArchetype.Create(EntityManager, _data.EventComponent, typeInfo, Allocator.Persistent);
                index = _data._batchCount++;
                _data.Batches.ResizeUninitialized(_data._batchCount);
                _data.Batches[index] = batch;
                _data.TypeIndexToBatchMap[key] = index;
                return batch;
            }
            return _data.Batches[index];
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
            if (!_data.TypeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = EventArchetype.Create<TComponent, TBufferData>(EntityManager, _data.EventComponent, Allocator.Persistent);
                index = _data._batchCount++;
                _data.Batches.ResizeUninitialized(_data._batchCount);
                _data.Batches[index] = batch;
                _data.TypeIndexToBatchMap[key] = index;
                return batch;
            }
            return _data.Batches[index];
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
