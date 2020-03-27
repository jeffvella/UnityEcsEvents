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
            public NativeList<EventArchetype> Buffer;
            public UnsafeList<Entity> Entities;
            public UnsafeNativeArray EntitySlicer;
            public UnsafeList<ArchetypeChunk> Chunks;
            public UnsafeNativeArray ChunkSlicer;
            public UnsafeEntityManager UnsafeEntityManager;
            public EntityArchetype DisabledArchetype;
            public ArchetypeChunkComponentType<EntityEvent> _archetypeComponentTypeStub;
            public int _batchCount;
            public int DisabledTypeIndex;
            public UnsafeNativeArray TempArray;
            public UnsafeAppendBuffer ChunkScratch;
            public UnsafeAppendBuffer BatchScratch;
            public UnsafeList CreateChunks;
            public UnsafeList AddComponentToChunks;
            public UnsafeList RemoveComponentFromChunks;
            internal UnsafeList AddComponentBatches;
            internal UnsafeList RemoveComponentBatches;
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
            data.Buffer = new NativeList<EventArchetype>(StartingBatchCount, Allocator.Persistent);
            data.Entities = new UnsafeList<Entity>(StartingBatchCount, Allocator.Persistent);
            data.EntitySlicer = data.Entities.ToUnsafeNativeArray();
            data.Chunks = new UnsafeList<ArchetypeChunk>(0, Allocator.Persistent);
            data.ChunkSlicer = data.Entities.ToUnsafeNativeArray();
            data.UnsafeEntityManager = new UnsafeEntityManager(EntityManager);
            data.DisabledTypeIndex = TypeManager.GetTypeIndex<Disabled>();
            data.TempArray = new UnsafeNativeArray();
            data.TempArray.m_Safety = AtomicSafetyHandle.Create();

            data.ChunkScratch = new UnsafeAppendBuffer(4096, 4, Allocator.Persistent);
            data.BatchScratch = new UnsafeAppendBuffer(4096, 4, Allocator.Persistent);

            data.CreateChunks = new UnsafeList(Allocator.Persistent);
            data.AddComponentToChunks = new UnsafeList(Allocator.Persistent);
            data.RemoveComponentFromChunks = new UnsafeList(Allocator.Persistent);
            data.AddComponentBatches = new UnsafeList(Allocator.Persistent);
            data.RemoveComponentBatches = new UnsafeList(Allocator.Persistent);

            data.DisabledArchetype = EntityManager.CreateArchetype(
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
            _data.Buffer.Dispose();
            _data.Entities.Dispose();
            _data.Chunks.Dispose();

            UnsafeUtility.Free(_dataPtr, Allocator.Persistent);
        }

        public struct Test
        {
            public int Value;
        }

        public struct EntityBatchInChunkProxy
        {
            public unsafe void* ChunkPtr;
            public int StartIndex;
            public int Count;
        }

        public struct RemoveComponentChunkOp
        {
            public unsafe void* Chunks;
            public int Count;
            public int TypeIndex;
        }

        public struct AddComponentChunkOp
        {
            public unsafe void* Chunks;
            public int Count;
            public int TypeIndex;
        }

        public struct RemoveComponentBatchOp
        {
            public UnsafeList* EntityBatches;
            public int TypeIndex;
        }

        public struct AddComponentBatchOp
        {
            public UnsafeList* EntityBatches;
            public int TypeIndex;
        }

        public struct CreateChunksOp
        {
            public EntityArchetype Archetype;
            public void* ChunksOut;
            public int EntityCount;
        }

        //public struct DestroyChunkOperation
        //{
        //    public EntityArchetype Archetype;
        //    public void* ChunksOut;
        //    public int EntityCount;
        //}

        protected override void OnUpdate()
        {
            if (_data._batchCount == 0)
                return;



            //var countSW = Stopwatch.StartNew();
            //if (_data.Chunks.Length > 0)
            //{
            //    for (int i = 0; i < _data.Chunks.Length; i++)
            //    {
            //        _data.Chunks.Ptr[i].Invalid(); // triggers null reference exception rather than editor crash.
            //    }

            //    // todo, exclude batches that will need the events again from last frame, we can just set data on them without any other processing.
            //    _data.UnsafeEntityManager.AddComponentToChunks(_data.Chunks.Ptr, _data.Chunks.Length, _data.DisabledTypeIndex);
            //    _data.Chunks.Clear();
            //}
            //countSW.Stop();
            //Debug.Log($"Destroy took {countSW.Elapsed.TotalMilliseconds:N4}");

            //var countSW1 = Stopwatch.StartNew();

            //var handle = GCHandle.Alloc(_data._batches, GCHandleType.Pinned);
            //var handle2 = GCHandle.Alloc(_data._chunks, GCHandleType.Pinned);  

            var prepTimer = Stopwatch.StartNew();

            var batches = _data.Batches;
            var dataPtr = _dataPtr;

            Job.WithCode(() =>
            {
                ref var data = ref UnsafeUtilityEx.AsRef<EventSystemData>(dataPtr);

                data.CreateChunks.Clear();
                data.AddComponentToChunks.Clear();
                data.RemoveComponentFromChunks.Clear();
                data.AddComponentBatches.Clear();
                data.RemoveComponentBatches.Clear();
                data.BatchScratch.Reset();

                for (int i = 0; i < data.Batches.Length; i++)
                {
                    //var bat = (EventArchetype*)data.Batches.GetUnsafePtr();

                    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(batches.GetUnsafePtr(), i);

                    var requiredEntities = data.Batches[i].ComponentQueue.ComponentCount();

                    batch.RequiresActiveUpdate = false;
                    batch.RequiresInactiveUpdate = false;

                    var previousEntityCount = batch.EntityCount;
                    if (previousEntityCount == requiredEntities)
                    {
                        return;
                    }
                    batch.EntityCount = requiredEntities;

                    if (requiredEntities == 0)
                    {
                        // Deactivate all
                        if (batch.ActiveChunks.Length != 0)
                        {
                            data.AddComponentToChunks.Add(new AddComponentChunkOp
                            {
                                Chunks = batch.ActiveArchetypeChunks.Ptr,
                                TypeIndex = data.DisabledTypeIndex,
                                Count = batch.ActiveArchetypeChunks.Length
                            });
                            batch.RequiresInactiveUpdate = true;
                        }
                        return;
                    }

                    var capacity = batch.Archetype.ChunkCapacity;
                    var requiredFullChunks = requiredEntities / capacity;
                    var fitsExactlyInChunkCapacity = requiredEntities % capacity == 0;
                    var requiredPartialChunks = fitsExactlyInChunkCapacity ? 0 : 1;
                    var requiredChunks = requiredFullChunks + requiredPartialChunks;

                    if (fitsExactlyInChunkCapacity) // this fails because it doesn't destroy existing partial actives
                    {
                        batch.RequiresActiveUpdate = true;
                        batch.RequiresInactiveUpdate = true;

                        // First, leave any already active chunks as they are.
                        var excessChunks = requiredChunks - batch.ActiveFullArchetypeChunks.Length; //activefull
                        if (excessChunks == 0)
                        {
                            //excess = 0 do nothing.

                            return;
                        }
                        else if (excessChunks > 0)
                        {
                            // convert some inactive chunks, and/or create more.

                            if (batch.InactiveFullArchetypeChunks.Length == 0)
                            {
                                // No full inactive chunks exist to re-use, so create all fresh chunks.

                                data.CreateChunks.Add(new CreateChunksOp
                                {
                                    Archetype = batch.Archetype,
                                    EntityCount = excessChunks * capacity,
                                });

                                batch.RequiresActiveUpdate = true;
                            }
                            else
                            {
                                // untested **

                                // Some inactive chunks exist that can be re-used

                                var chunksToConvert = math.min(excessChunks, batch.InactiveChunks.Length); // e.g. need 30 but only have 5 to convert. or have 30 and only need 5.
                                data.RemoveComponentFromChunks.Add(new RemoveComponentChunkOp
                                {
                                    Chunks = batch.InactiveFullArchetypeChunks.Ptr,
                                    TypeIndex = data.DisabledTypeIndex,
                                    Count = chunksToConvert,
                                });

                                excessChunks -= chunksToConvert;
                                if (excessChunks > 0)
                                {
                                    // Additional chunks are still needed, create new ones.

                                    //var remainingEntities = requiredEntities - (conversionFullChunks * capacity);
                                    data.CreateChunks.Add(new CreateChunksOp
                                    {
                                        Archetype = batch.Archetype,
                                        EntityCount = excessChunks * capacity,
                                    });
                                }

                                batch.RequiresActiveUpdate = true;
                                batch.RequiresInactiveUpdate = true;
                            }
                        }
                        else
                        {
                            // destroy excess active chunks.

                            data.AddComponentToChunks.Add(new AddComponentChunkOp
                            {
                                Chunks = batch.ActiveFullArchetypeChunks.Ptr,
                                TypeIndex = data.DisabledTypeIndex,
                                Count = excessChunks * -1
                            });

                            // partial ptrs cat be given to chunks method >> must be ArchetypeChunk
                            //data.AddComponentToChunks.Add(new AddComponentChunkOp 
                            //{
                            //    Chunks = batch.ActivePartialArchetypeChunkPtrs.Ptr,
                            //    TypeIndex = data.DisabledTypeIndex,
                            //    Count = batch.ActivePartialArchetypeChunkPtrs.Length
                            //});

                            // destroy partial active chunks here to
                            batch.RequiresActiveUpdate = true;
                            batch.RequiresInactiveUpdate = true;
                        }
                    }
                    else //if (requiredChunks == 1)
                    {
  

                        // temp for now
                        batch.RequiresActiveUpdate = true;
                        batch.RequiresInactiveUpdate = true;

                        var remainingEntities = requiredEntities;
                        var remainingInactiveFulls = batch.InactiveFullArchetypeChunks.Length;

                        // -------------  Handle Full Chunks.

                        if (requiredFullChunks > 0) // at least one full must be created. // check any active/inactive full to bypass?
                        {
                            var remainingFullChunks = requiredFullChunks;

                            // Keep Active Fulls
                            if (batch.ActiveFullArchetypeChunks.Length != 0)
                            {
                                var kept = math.min(remainingFullChunks, batch.ActiveFullArchetypeChunks.Length);
                                remainingFullChunks -= kept;

                                var excessActiveChunks = batch.ActiveFullArchetypeChunks.Length - kept;
                                if (excessActiveChunks != 0)
                                {
                                    // Deactivate excess full active chunks
                                    data.AddComponentToChunks.Add(new AddComponentChunkOp
                                    {
                                        Chunks = batch.ActiveFullArchetypeChunks.Ptr,
                                        Count = excessActiveChunks,
                                        TypeIndex = data.DisabledTypeIndex,
                                    });
                                }
                            }

                            if (batch.ActivePartialArchetypeChunk.Length != 0)
                            {
                                // Deactivate active partials
                                data.AddComponentToChunks.Add(new AddComponentChunkOp
                                {
                                    Chunks = batch.ActivePartialArchetypeChunk.Ptr,
                                    Count = batch.ActivePartialArchetypeChunk.Length,
                                    TypeIndex = data.DisabledTypeIndex,
                                });
                            }

                            // Convert Inactive Fulls
                            if (remainingFullChunks > 0 && remainingInactiveFulls != 0) // used count for later
                            {
                                var conversionCount = math.min(remainingFullChunks, batch.InactiveFullArchetypeChunks.Length); // fulls only
                                data.RemoveComponentFromChunks.Add(new RemoveComponentChunkOp
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
                            data.AddComponentToChunks.Add(new AddComponentChunkOp
                            {
                                Chunks = batch.ActiveArchetypeChunks.Ptr,
                                Count = batch.ActiveArchetypeChunks.Length,
                                TypeIndex = data.DisabledTypeIndex,
                            });
                        }

                        // -------------  Handle Partial Chunks.

                        if (remainingEntities > 0 && batch.InactiveChunks.Length != 0) // if there are some inactives to convert
                        {
                            // pull from inactives (partials firs then fulls)

                            // ---
                            NativeList<EntityBatchInChunkProxy> batchInChunks = new NativeList<EntityBatchInChunkProxy>(Allocator.Temp);

                            // Create actives from 1-n inactive partials

                            if (batch.InactivePartialArchetypeChunk.Length != 0) // create full chunk sized pieces out before this
                            {
                                //remainingEntities = requiredEntities;
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

                                data.RemoveComponentBatches.Add(new RemoveComponentBatchOp
                                {
                                    EntityBatches = batchInChunks.AsParallelWriter().ListData,
                                    TypeIndex = data.DisabledTypeIndex,
                                });
                            }

                            if (remainingEntities > 0 && remainingInactiveFulls != 0) 
                            {
                                // Create actives from 1-n inactive fulls

                                batchInChunks.Clear();

                                //remainingEntities = requiredEntities;// remove
                                //for (int j = 0; j < batch.InactiveFullArchetypeChunks.Length; j++) // potentially using an inactive full that is already queued for full chunk conversion 
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

                                data.RemoveComponentBatches.Add(new RemoveComponentBatchOp
                                {
                                    EntityBatches = batchInChunks.AsParallelWriter().ListData,
                                    TypeIndex = data.DisabledTypeIndex,
                                });
                            }
                        }

                        if (remainingEntities != 0) // todo: currently not coping from active partials to avoid create.
                        {
                            data.CreateChunks.Add(new CreateChunksOp
                            {
                                Archetype = batch.Archetype,
                                EntityCount = remainingEntities,
                            });
                        }
                    }
                    //else if (batch.InactivePartialArchetypeChunkPtrs.Length > 0) // no actives to re-use
                    //{
                    //    // convert inactive partials up to what we need.

                    //    ref var inputPtrs = ref batch.InactivePartialArchetypeChunkPtrs;
                    //    ref var outputOps = ref data.RemoveComponentBatches;

                    //    var len = inputPtrs.Length;
                    //    var batchInChunks = new NativeList<EntityBatchInChunkProxy>(len, Allocator.Temp);

                    //    var remaining = requiredEntities;
                    //    for (int j = 0; j < len; j++) // consume inactive partial chunks until the required amount have been moved.
                    //    {
                    //        var archetypeChunkPtr = (ArchetypeChunk*)inputPtrs.Ptr[j];
                    //        var amountToMove = math.min(archetypeChunkPtr->Count, remaining);
                    //        var entityBatch = new EntityBatchInChunkProxy
                    //        {
                    //            ChunkPtr = archetypeChunkPtr->GetChunkPtr(),
                    //            Count = amountToMove,
                    //            StartIndex = 0,
                    //        };
                    //        batchInChunks.Add(entityBatch);
                    //        remaining -= amountToMove;
                    //        if (remaining == 0)
                    //            break;
                    //    }

                    //    outputOps.Add(new RemoveComponentBatchOp
                    //    {
                    //        EntityBatches = batchInChunks.AsParallelWriter().ListData,
                    //        TypeIndex = data.DisabledTypeIndex,
                    //    });

                    //    if (remaining > 0)
                    //    {
                    //        data.CreateChunks.Add(new CreateChunksOp
                    //        {
                    //            Archetype = batch.Archetype,
                    //            EntityCount = remaining,
                    //        });
                    //    }
                    //}
                    //else // No active or inactive partials
                    //{
                    //    if (batch.ActiveFullArchetypeChunks.Length > 0)
                    //    {
                    //        // split an active full chunk.
                    //    }
                    //    // >> try to pull from an inactive full chunk
                    //    else if (batch.InactiveFullArchetypeChunks.Length > 0)
                    //    {
                    //        // consume from an inactive full
                    //    }
                    //    else
                    //    {

                    //    }

                    //    if (remaining > 0)
                    //    {

                    //    }

                    //    if (batch.ActiveFullArchetypeChunks.Length > 0) // take active fulls out of the picture.
                    //    {
                    //        // Remove Active Fulls.
                    //        data.AddComponentToChunks.Add(new AddComponentChunkOp
                    //        {
                    //            Chunks = batch.ActiveFullArchetypeChunks.Ptr,
                    //            TypeIndex = data.DisabledTypeIndex,
                    //            Count = batch.ActiveFullArchetypeChunks.Length
                    //        });
                    //    }

                    //    // >> last resort, create new entities.

                    //    data.CreateChunks.Add(new CreateChunksOp
                    //    {
                    //        Archetype = batch.Archetype,
                    //        EntityCount = requiredEntities,
                    //    });
                    //}



                    //else
                    //{
                    //    //// produce at least 1 full chunk + at least 1 partial chunk.
                    //    //data.CreateChunkOperations.Add(new CreateChunksOperation
                    //    //{
                    //    //    Archetype = batch.Archetype,
                    //    //    EntityCount = requiredEntities,
                    //    //});

                    //    //batch.RequiresActiveUpdate = true;

                    //    // temp for now
                    //    batch.RequiresActiveUpdate = true;
                    //    batch.RequiresInactiveUpdate = true;

                    //    data.CreateChunks.Add(new CreateChunksOp
                    //    {
                    //        Archetype = batch.Archetype,
                    //        EntityCount = requiredEntities,
                    //    });

                    //}
                }

            }).Run();

            prepTimer.Stop();

            var opsTimer = Stopwatch.StartNew();

            for (int i = 0; i < _data.AddComponentToChunks.Length; i++)
            {
                var op = ((AddComponentChunkOp*)_data.AddComponentToChunks.Ptr)[i];

                _data.UnsafeEntityManager.AddComponentToChunks(op.Chunks, op.Count, op.TypeIndex);
            }

            for (int i = 0; i < _data.RemoveComponentFromChunks.Length; i++)
            {
                var op = ((RemoveComponentChunkOp*)_data.RemoveComponentFromChunks.Ptr)[i];

                _data.UnsafeEntityManager.RemoveComponentFromChunks(op.Chunks, op.Count, op.TypeIndex);
            }

            for (int i = 0; i < _data.CreateChunks.Length; i++)
            {
                var op = ((CreateChunksOp*)_data.CreateChunks.Ptr)[i];
                _data.ChunkScratch.ResizeUninitialized(op.EntityCount * sizeof(Entity));
                _data.TempArray.m_Buffer = _data.ChunkScratch.Ptr;
                _data.TempArray.m_Length = op.EntityCount;

                // CreateEntity is faster than CreateChunk because the chunk code doesn't go through burst.
                EntityManager.CreateEntity(op.Archetype, _data.TempArray.AsNativeArray<Entity>());
            }

            for (int i = 0; i < _data.RemoveComponentBatches.Length; i++)
            {
                var op = ((RemoveComponentBatchOp*)_data.RemoveComponentBatches.Ptr)[i];

                //for (int i = 0; i < op.EntityBatchInChunk->Length; i++)
                //    ((ArchetypeChunk*)op.EntityBatchInChunk->Ptr)[0].Invalid();

                _data.UnsafeEntityManager.RemoveComponentEntitiesBatch(op.EntityBatches, op.TypeIndex);
            }

            for (int i = 0; i < _data.AddComponentBatches.Length; i++)
            {
                var op = ((AddComponentBatchOp*)_data.AddComponentBatches.Ptr)[i];

                //for (int i = 0; i < op.EntityBatchInChunk->Length; i++)
                //    ((ArchetypeChunk*)op.EntityBatchInChunk->Ptr)[0].Invalid();

                _data.UnsafeEntityManager.AddComponentEntitiesBatch(op.EntityBatches, op.TypeIndex);
            }



            opsTimer.Stop();

            var scanTimer = Stopwatch.StartNew();

            Job.WithCode(() =>
            {
                ref var data = ref UnsafeUtilityEx.AsRef<EventSystemData>(dataPtr);

                for (int i = 0; i < data.Batches.Length; i++)
                {
                    ref EventArchetype batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(batches.GetUnsafePtr(), i);

                    batch.UpdateChunkCollections();
                }

            }).Run();

            scanTimer.Stop();

            var setTimer = Stopwatch.StartNew();

            //if (_data..Length == 0)
            //    return;

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

            setTimer.Stop();

            Debug.Log($"Prep={prepTimer.Elapsed.TotalMilliseconds:N4} Ops={opsTimer.Elapsed.TotalMilliseconds:N4} Scan={scanTimer.Elapsed.TotalMilliseconds:N4} Set={setTimer.Elapsed.TotalMilliseconds:N4}");

            #region backsups

            ////var ptr = _data.Batches.GetUnsafePtr();
            ////for (int i = 0; i < _data.Batches.Length; i++)
            ////{
            ////    ref var batch = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);
            ////    var requiredEntities = batch.ComponentQueue.ComponentCount();
            ////    if (requiredEntities == 0)
            ////        continue;

            ////    var cap = batch.Archetype.ChunkCapacity;
            ////    var requiredFullChunks = requiredEntities / cap;
            ////    var fitsInSingleChunk = requiredEntities % cap == 0;
            ////    var requiredPartialChunks = fitsInSingleChunk ? 0 : 1;
            ////    var requiredChunks = requiredFullChunks + requiredPartialChunks;
            ////    var conversionFullChunks = math.min(requiredChunks, batch.FullChunks.Length);

            ////    if (requiredChunks == 1)
            ////    {
            ////        if (fitsInSingleChunk)
            ////        {
            ////            // produce 1 full chunk of entities
            ////        }
            ////        else
            ////        {
            ////            // produce a partial chunk
            ////        }
            ////    }
            ////    else 
            ////    {
            ////        // produce at least 1 full chunk + at least 1 partial chunk.
            ////    }

            ////    //// Create entities by removing the disabled component from otherwise identical full chunks.
            ////    //_data._unsafeEntityManager.RemoveComponentFromChunks(batch.FullChunks.Ptr, conversionFullChunks, _data._disabledTypeIndex);
            ////    //var remainingChunks = requiredChunks - conversionFullChunks;
            ////    //var remainingEntities = requiredEntities - (conversionFullChunks * cap); 


            ////    if (remainingEntities > 0)
            ////    {
            ////        // Create entities from partial inactive chunks.
            ////        // todo: evaluate if 1 partial only ever exists, if so, this can be simplified considerably.
            ////        if (batch.PartialChunks.Length > 0)
            ////        {
            ////            //var conversionsRemaining = remainingEntities;
            ////            //var entitiesConverted = 0;
            ////            //var chunksDestroyed = 0;
            ////            //var chunksCreated = 0;
            ////            //var batchInChunks = new NativeList<EntityBatchInChunkProxy>(batch.PartialChunks.Length, Allocator.Temp);
            ////            //var startingChunkCount = batch.ActiveChunks.ChunkCount;

            ////            //for (int j = batch.PartialChunks.Length - 1; j != -1; j--)
            ////            //{
            ////            //    var partial = batch.PartialChunks.Ptr[j];

            ////            //    //Debug.Assert(!partial.Invalid());

            ////            //    var amountToMove = math.min(partial.Count, conversionsRemaining);

            ////            //    batchInChunks.Add(new EntityBatchInChunkProxy // todo: try to store these up for all _batches and do them once together at the end.
            ////            //    {
            ////            //        Chunk = partial.GetChunkPtr(),
            ////            //        Count = amountToMove,
            ////            //        StartIndex = 0
            ////            //    });

            ////            //    if (amountToMove == partial.Count)
            ////            //        chunksDestroyed++;

            ////            //    chunksCreated++;
            ////            //    entitiesConverted += amountToMove;
            ////            //    conversionsRemaining -= amountToMove;

            ////            //    if (conversionsRemaining <= 0)
            ////            //        break;
            ////            //}

            ////            //_data._unsafeEntityManager.RemoveComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _data._disabledTypeIndex);
            ////            //remainingEntities -= entitiesConverted;
            ////            //var chunksConverted = entitiesConverted / cap + entitiesConverted % cap == 0 ? 0 : 1; 
            ////            //remainingChunks -= chunksConverted;

            ////            //batch.PartialChunks.Length -= chunksDestroyed;

            ////            //if (startingChunkCount != batch.ActiveChunks.ChunkCount)
            ////            //{
            ////            //    var idx = math.max(0, batch.ActiveChunks.ChunkCount - chunksCreated);
            ////            //    while (idx != batch.ActiveChunks.ChunkCount) // possibly because active chunks length = 1, so it started -2
            ////            //    {
            ////            //        var chunk = batch.ActiveChunks[idx++];
            ////            //        if (!chunk.Full)
            ////            //            batch.PartialChunks.Add(chunk); // this is wrong, added 2x destroyed chunks to partials.
            ////            //    }
            ////            //}

            ////        }

            ////        // todo - evaluate if there is ever a case were partials need to be consumed (above) and then more chunks created (below) and simplify if false.
            ////        if (remainingEntities > 0) // Create new chunks. 
            ////        {

            ////            //// Create entities from partial inactive chunks.
            ////            //if (batch.PartialChunks.Length > 0)
            ////            //{
            ////            //    var entitiesConverted = 0;
            ////            //    var chunksDestroyed = 0;
            ////            //    var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.Temp);

            ////            //    for (int j = batch.PartialChunks.Length; j != 0; j--)
            ////            //    {
            ////            //        var partial = batch.PartialChunks.Ptr[j];
            ////            //        batchInChunks.Add(new EntityBatchInChunkProxy
            ////            //        {
            ////            //            Chunk = partial.GetChunkPtr(),
            ////            //            Count = partial.Count,
            ////            //            StartIndex = 0
            ////            //        });

            ////            //        entitiesConverted += partial.Count;
            ////            //        if (entitiesConverted >= remainingEntities)
            ////            //            break;
            ////            //    }

            ////            //    // todo: see if there's a way to do all of this building work in burst, store it out into an instruction collection
            ////            //    // and then loop through the removes outside burst afterwards, or find a way to call the burst ptrs from within burst / FuncitonPointer<T>
            ////            //    _data._unsafeEntityManager.RemoveComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _data._disabledTypeIndex);
            ////            //    remainingEntities -= entitiesConverted;
            ////            //    remainingChunks -= entitiesConverted / cap;
            ////            //    batch.PartialChunks.Length -= chunksDestroyed; // all full chunks list should be full -check
            ////            //}

            ////            //// Ensure the batch can contain all the chunks we need.
            ////            //batch.FullChunks.Resize(batch.FullChunks.Length + remainingChunks);

            ////            //// Configure a resuable/temporary NativeArray because CreateChunk requires a NativeArray...
            ////            //_data._batchArray.m_Buffer = (byte*)batch.FullChunks.Ptr + conversionFullChunks * sizeof(ArchetypeChunk);
            ////            //_data._batchArray.m_Length = remainingChunks;

            ////            //// Copy new chunks directly into the batch.
            ////            //EntityManager.CreateChunk(_data.Batches[i].Archetype, _data._batchArray.AsNativeArray<ArchetypeChunk>(), remainingEntities);

            ////            //if (requiredPartialChunks > 0)
            ////            //{
            ////            //    // Copy the last created into the partial group
            ////            //    batch.PartialChunks.Add(batch.FullChunks.Ptr[batch.FullChunks.Length - 1]);

            ////            //    // Bump the partial chunk off the end of the FullChunks list.
            ////            //    batch.FullChunks.Length--;
            ////            //}
            ////        }
            ////    }
            ////    else // Split an activated full chunk into two partials by removing some.
            ////    {
            ////        //var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.Temp);
            ////        //var partialIndex = conversionFullChunks - 1;
            ////        //var activePartial = batch.FullChunks.Ptr[partialIndex];
            ////        //var inactiveCount = batch.InactiveChunks.ChunkCount;

            ////        //batchInChunks.Add(new EntityBatchInChunkProxy
            ////        //{
            ////        //    Chunk = activePartial.GetChunkPtr(),
            ////        //    Count = remainingEntities * -1,
            ////        //    StartIndex = remainingEntities + cap
            ////        //});

            ////        //// todo, can this be processed for all batches later and the builder logic moved into burst?
            ////        //_data._unsafeEntityManager.AddComponentEntitiesBatch(batchInChunks.AsParallelWriter().ListData, _data._disabledTypeIndex);

            ////        //if (requiredPartialChunks > 0)
            ////        //{
            ////        //    // A split of full chunk makes two partials

            ////        //    //if (inactivePartial.Full) // a new is only created if there isnt viable space in an existing chunk.
            ////        //    //{
            ////        //    //    Debug.Log("Full after split - Added to existing chunk");
            ////        //    //}
            ////        //    //if (inactiveCount == batch.InactiveChunks.Length)
            ////        //    //{
            ////        //    //    Debug.Log("Same counts after split - Added to existing chunk");
            ////        //    //}
            ////        //    var inactivePartial = batch.InactiveChunks.Last(); // todo: ??? scan back through to find the last partial and/or check .Full
            ////        //    if (inactiveCount == batch.InactiveChunks.ChunkCount) // a new chunk is not always created.
            ////        //    {
            ////        //        // this means that another chunk in the partial collection might be now full.
            ////        //        batch.PartialChunks.Add(inactivePartial); // getting multiple chunks that are full in PartialChunks.
            ////        //    }

            ////        //    // Bump the partial chunk off the end of the FullChunks list.
            ////        //    batch.PartialChunks.Add(activePartial);
            ////        //    batch.FullChunks.RemoveAtSwapBack(partialIndex);
            ////        //}
            ////    }

            ////    // todo: is it necessary or useful to include all chunks, even if they're not used or can this just copy from the active view.
            ////    //var offset = _chunks.Length * sizeof(ArchetypeChunk);
            ////    //int chunkCount = _chunks.Length + batch.FullChunks.Length + batch.PartialChunks.Length;
            ////    //_chunks.Resize(chunkCount);
            ////    //var size1 = batch.FullChunks.Length * sizeof(ArchetypeChunk);
            ////    //var size2 = batch.PartialChunks.Length * sizeof(ArchetypeChunk);
            ////    //var startPtr = (byte*)_chunks.Ptr + offset;
            ////    //UnsafeUtility.MemCpy(startPtr, batch.FullChunks.Ptr, size1);
            ////    //UnsafeUtility.MemCpy(startPtr + size1, batch.PartialChunks.Ptr, size2);

            ////    //todo need a test to make sure multiple batches are dumping into the combined _chunks list.

            ////    //var tmp = new UnsafeList();
            ////    //var swX = Stopwatch.StartNew();
            ////    //Job.WithCode(() =>
            ////    //{
            ////    //ref var batch1 = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);

            ////    //batch1.FullChunks.Clear();
            ////    //batch1.PartialChunks.Clear();
            ////    //var idx = batch1.ActiveChunks.ChunkCount;
            ////    //while (--idx != -1)
            ////    //{
            ////    //    ref var chunk = ref batch1.ActiveChunks[idx];
            ////    //    chunk.Invalid();
            ////    //    if (chunk.Full)
            ////    //        batch1.FullChunks.Add(chunk);
            ////    //    else
            ////    //        batch1.PartialChunks.Add(chunk);
            ////    //}

            ////    //}).Run();
            ////    //swX.Stop();
            ////    //Debug.Log($"Chunk Filter took {swX.Elapsed.TotalMilliseconds:N4}");



            ////    //batch.FullChunks.Clear();
            ////    //batch.PartialChunks.Clear();

            ////    //foreach (ref var chunk in batch.ActiveChunks.GetEnumerator(ChunkFilter.Full))
            ////    //{
            ////    //    batch.FullChunks.Add(chunk);
            ////    //}

            ////    //foreach (ref var chunk in batch.ActiveChunks.GetEnumerator(ChunkFilter.Partial))
            ////    //{
            ////    //    batch.PartialChunks.Add(chunk);
            ////    //}

            //    var dataPtr = _dataPtr;
            //    var swX = Stopwatch.StartNew();

            //    Job.WithCode(() =>
            //    {
            //        ref var batch1 = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);
            //        ref var data = ref UnsafeUtilityEx.AsRef<EventSystemData>(dataPtr);

            //        batch1.InactiveFull.Clear();
            //        batch1.ActiveFull.Clear();
            //        batch1.InactivePartial.Clear();
            //        batch1.ActivePartial.Clear();

            //        ArchetypeChunk* ptr1;

            //        var len = batch1.ActiveChunks.ChunkCount;
            //        for (int x = 0; x < len; x++)
            //        {
            //            ptr1 = batch1.ActiveChunks.GetChunkPtr(x);
            //            if (ptr1->Full)
            //            {
            //                batch1.ActiveFull.Add(*ptr1);
            //            } 
            //            else
            //            {
            //                batch1.ActivePartial.Add(ptr1);
            //            }
            //        }

            //        len = batch1.InactiveChunks.ChunkCount;
            //        for (int x = 0; x < len; x++)
            //        {
            //            ptr1 = batch1.InactiveChunks.GetChunkPtr(x);
            //            if (ptr1->Full)
            //            {
            //                batch1.InactiveFull.Add(*ptr1);
            //            }
            //            else
            //            {
            //                batch1.InactivePartial.Add(ptr1);
            //            }
            //        }

            //        batch1.ActiveChunks.AddTo(ref data._chunks);

            //        //var enu = batch1.ActiveChunks.GetEnumerator();
            //        //while(enu.MoveNext())
            //        //{
            //        //    if (enu.Current.Full)
            //        //        batch1.FullChunks.Add(enu.Current);
            //        //    else
            //        //        batch1.PartialChunks.Add(enu.Current);
            //        //}

            //    }).Run();

            //    swX.Stop();
            //    Debug.Log($"ActiveChunks.AddTo took {swX.Elapsed.TotalMilliseconds:N4}");


            //    //for (int x = 0; x < _chunks.Length; x++)
            //    //   _chunks.Ptr[x].Invalid();

            //    //var test = new NativeArray<ArchetypeChunk>(batch.ActiveChunks.Length, Allocator.Temp);
            //    //batch.ActiveChunks.CopyTo(test.GetUnsafePtr());



            //    //batch.ActiveChunks.AddTo(ref _data._chunks);

            //    //Job.WithCode(() =>
            //    //{
            //    //    ref var batch1 = ref UnsafeUtilityEx.ArrayElementAsRef<EventArchetype>(ptr, i);
            //    //    ref var data = ref UnsafeUtilityEx.AsRef<EventSystemData>(dataPtr);

            //    //    batch1.ActiveChunks.AddTo(ref data._chunks);

            //    //}).Run();



            //    //_chunks.AddRange(batch.FullChunks);// add active chunks from view isntead? some of these are unused.
            //    //_chunks.AddRange(batch.PartialChunks);
            //    // or maybe split _chunks into two, re-used fulls can then avoid being switched archetype twice.
            //    //batch.ComponentQueue.Clear();

            //    foreach (var chunk in batch.ActiveChunks)
            //        chunk.Invalid();
            //    foreach (var chunk in batch.InactiveChunks)
            //        chunk.Invalid();
            //}

            //countSW1.Stop();
            //Debug.Log($"Create Entities took {countSW1.Elapsed.TotalMilliseconds:N4}");

            //var countSW2 = Stopwatch.StartNew();


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

            #endregion
        }

        private static void ActivateEntities(ref UnsafeList outputOps, ref UnsafePtrList inputPtrs, int entityCount, int typeIndex)
        {
            var len = inputPtrs.Length;
            var batchInChunks = new NativeList<EntityBatchInChunkProxy>(len, Allocator.Temp);

            var remaining = entityCount;
            for (int j = 0; j < len; j++) // consume inactive partial chunks until the required amount have been moved.
            {
                var archetypeChunkPtr = (ArchetypeChunk*)inputPtrs.Ptr[j];
                var amountToMove = math.min(archetypeChunkPtr->Count, remaining);
                var entityBatch = new EntityBatchInChunkProxy
                {
                    ChunkPtr = archetypeChunkPtr->GetChunkPtr(),
                    Count = amountToMove,
                    StartIndex = 0,
                };
                batchInChunks.Add(entityBatch);
                remaining -= amountToMove;
                if (remaining == 0)
                    break;
            }

            outputOps.Add(new RemoveComponentBatchOp
            {
                EntityBatches = batchInChunks.AsParallelWriter().ListData,
                TypeIndex = typeIndex,
            });
        }

        //private static void DeactivateEntities(ref UnsafeList outputOps, ref UnsafePtrList archetypeChunks, int activesRequired, int typeIndex)
        //{
        //    var len = archetypeChunks.Length;
        //    var batchInChunks = new NativeList<EntityBatchInChunkProxy>(len, Allocator.Temp);

        //    // chunk of actives with 10
        //    // need only 3 actives
        //    // >> add batch from index 3, moving 7 actives.

        //    //var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.Temp);
        //    //var partialIndex = conversionFullChunks - 1;
        //    //var activePartial = batch.FullChunks.Ptr[partialIndex];
        //    //var inactiveCount = batch.InactiveChunks.ChunkCount;

        //    //batchInChunks.Add(new EntityBatchInChunkProxy
        //    //{
        //    //    Chunk = activePartial.GetChunkPtr(),
        //    //    Count = remainingEntities * -1,
        //    //    StartIndex = remainingEntities + cap
        //    //});

        //    var remaining = activesRequired;
        //    for (int j = 0; j < len; j++)
        //    {
        //        var archetypeChunkPtr = (ArchetypeChunk*)archetypeChunks.Ptr[j];
        //        var toMoveOut = archetypeChunkPtr->Count - remaining; 
        //        if (toMoveOut > 0)
        //        {
        //            var entityBatch = new EntityBatchInChunkProxy
        //            {
        //                ChunkPtr = archetypeChunkPtr->GetChunkPtr(),
        //                Count = toMoveOut,
        //                StartIndex = archetypeChunkPtr->Count - toMoveOut,
        //            };
        //        }

        //        //if (archetypeChunkPtr->Count)
        //        //var amountToMove = math.min(archetypeChunkPtr->Count, remaining);

        //        batchInChunks.Add(entityBatch);
        //        remaining -= amountToMove;
        //        if (remaining == 0)
        //            break;
        //    }

        //    outputOps.Add(new AddComponentBatchOp
        //    {
        //        EntityBatches = batchInChunks.AsParallelWriter().ListData,
        //        TypeIndex = typeIndex,
        //    });
        //}


        //private static EventSystemData ConstructRemoveComponentBatch(ref EventSystemData data, ref EventArchetype batch, int requiredEntities)
        //{
        //    var len = batch.InactivePartialArchetypeChunkPtrs.Length;
        //    var list = new NativeList<EntityBatchInChunkProxy>(len, Allocator.Temp);

        //    //EntityBatchInChunkProxy* list = stackalloc EntityBatchInChunkProxy[len];

        //    var remaining = requiredEntities;
        //    for (int j = 0; j < len; j++)
        //    {
        //        var archetypeChunkPtr = (ArchetypeChunk*)batch.InactivePartialArchetypeChunkPtrs.Ptr[j];
        //        var amountToMove = math.min(archetypeChunkPtr->Count, remaining);
        //        var entityBatch = new EntityBatchInChunkProxy
        //        {
        //            ChunkPtr = archetypeChunkPtr->GetChunkPtr(),
        //            Count = amountToMove,
        //            StartIndex = 0,
        //        };
        //        list.Add(entityBatch);
        //        remaining -= amountToMove;
        //        if (remaining == 0)
        //            break;
        //    }

        //    //var length = requiredEntities - remaining;
        //    //var offset = data.BatchScratch.Length;
        //    //data.BatchScratch.AddArray<EntityBatchInChunkProxy>(list, length);
        //    //var arrTest = data.BatchScratch.AsReader().ReadNextArray<EntityBatchInChunkProxy>(out var len1);
        //    //var batchPtr = data.BatchScratch.Ptr + offset + sizeof(int); // (+ array length prefix)
        //    //var batchPtr = data.BatchScratch.AddUnsafeList(list, requiredEntities - remaining);
        //    //var debug1 = UnsafeUtilityEx.AsRef<UnsafeList<EntityBatchInChunkProxy>>(batchPtr);

        //    data.RemoveComponentBatches.Add(new RemoveComponentBatchOp
        //    {
        //        EntityBatches = list.AsParallelWriter().ListData,
        //        TypeIndex = data.DisabledTypeIndex,
        //    });
        //    return data;
        //}



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
        public EventQueue<T> GetQueue<T>(int startingPoolSize = 0) where T : struct, IComponentData
        {
            return GetOrCreateBatch<T>(startingPoolSize).Batch.ComponentQueue.Cast<EventQueue<T>>();
        }

        /// <summary>
        /// Initialize an event type with starting capacity (mostly just for debug/test purposes).
        /// </summary>
        /// <typeparam name="T">type of event</typeparam>
        /// <param name="startingPoolSize">number of inactive entities to create</param>
        /// <returns>true if the batch was created, false if it was already there and therefore not created</returns>
        public bool InitializeEventType<T>(int startingPoolSize) where T : struct, IComponentData
        {
            return GetOrCreateBatch<T>(startingPoolSize).WasCreated;
        }

        //public EventQueue<T> InitializeBatch<T>(int startingPoolSize) where T : struct, IComponentData
        //{
        //    return GetOrCreateBatch<T>(startingPoolSize).ComponentQueue.Cast<EventQueue<T>>();
        //}

        /// <summary>
        /// Acquire an untyped shared queue for creating events within jobs.
        /// </summary>
        public EventQueue GetQueue(TypeManager.TypeInfo typeInfo)
        {
            return GetOrCreateBatch(typeInfo).Batch.ComponentQueue;
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
            return GetOrCreateBatch<TComponent,TBufferData>().Batch.ComponentQueue.Cast<EventQueue<TComponent,TBufferData>>();
        }

        private (EventArchetype Batch, bool WasCreated) GetOrCreateBatch<T>(int startingPoolSize = 0) where T : struct, IComponentData
        {
            int key = TypeManager.GetTypeIndex<T>();
            if (!_data.TypeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = EventArchetype.Create<T>(EntityManager, _data.EventComponent, startingPoolSize, Allocator.Persistent);
                index = _data._batchCount++;
                _data.Batches.ResizeUninitialized(_data._batchCount);
                _data.Batches[index] = batch;
                _data.TypeIndexToBatchMap[key] = index;
                return (batch, true);
            }
            return (_data.Batches[index], false);
        }

        public (EventArchetype Batch, bool WasCreated) GetOrCreateBatch(TypeManager.TypeInfo typeInfo, int startingPoolSize = 0)
        {
            int key = typeInfo.TypeIndex;
            if (!_data.TypeIndexToBatchMap.TryGetValue(key, out int index))
            {
                var batch = EventArchetype.Create(EntityManager, _data.EventComponent, startingPoolSize, typeInfo, Allocator.Persistent);
                index = _data._batchCount++;
                _data.Batches.ResizeUninitialized(_data._batchCount);
                _data.Batches[index] = batch;
                _data.TypeIndexToBatchMap[key] = index;
                return (batch, true);
            }
            return (_data.Batches[index], false);

            //int key = typeInfo.TypeIndex;
            //if (!_typeIndexToBatchMap.TryGetValue(key, out EventArchetype batch))
            //{
            //    batch = EventArchetype.Create(EntityManager, _eventComponent, typeInfo, Allocator.Persistent);
            //    _typeIndexToBatchMap[key] = batch;
            //    _batchCount++;
            //}
            //return batch;
        }

        private (EventArchetype Batch, bool WasCreated) GetOrCreateBatch<TComponent,TBufferData>(int startingPoolSize = 0)
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
                var batch = EventArchetype.Create<TComponent, TBufferData>(EntityManager, _data.EventComponent, startingPoolSize, Allocator.Persistent);
                index = _data._batchCount++;
                _data.Batches.ResizeUninitialized(_data._batchCount);
                _data.Batches[index] = batch;
                _data.TypeIndexToBatchMap[key] = index;
                return (batch, true);
            }
            return (_data.Batches[index], false);
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


//var remainingChunks = requiredChunks - batch.ActiveFullArchetypeChunks.Length; 
//if (remainingChunks == 0)
//{
//    //excess = 0 do nothing.
//    return;
//}
//else if (remainingChunks > 0)
//{
//    // convert some inactive chunks, and/or create more.

//    if (batch.InactiveFullArchetypeChunks.Length == 0)
//    {
//        // No full inactive chunks exist to re-use, so create all fresh chunks.

//        data.CreateChunks.Add(new CreateChunksOp
//        {
//            Archetype = batch.Archetype,
//            EntityCount = remainingChunks * capacity,
//        });

//        batch.RequiresActiveUpdate = true;
//    }
//    else
//    {
//        // untested **

//        // Some inactive chunks exist that can be re-used

//        var chunksToConvert = math.min(remainingChunks, batch.InactiveChunks.Length); // e.g. need 30 but only have 5 to convert. or have 30 and only need 5.
//        data.RemoveComponentFromChunks.Add(new RemoveComponentChunkOp
//        {
//            Chunks = batch.InactiveFullArchetypeChunks.Ptr,
//            TypeIndex = data.DisabledTypeIndex,
//            Count = chunksToConvert,
//        });

//        remainingChunks -= chunksToConvert;
//        if (remainingChunks > 0)
//        {
//            // Additional chunks are still needed, create new ones.

//            //var remainingEntities = requiredEntities - (conversionFullChunks * capacity);
//            data.CreateChunks.Add(new CreateChunksOp
//            {
//                Archetype = batch.Archetype,
//                EntityCount = remainingChunks * capacity,
//            });
//        }

//        batch.RequiresActiveUpdate = true;
//        batch.RequiresInactiveUpdate = true;
//    }
//}
//else
//{
//    // destroy excess active chunks.
//    data.AddComponentToChunks.Add(new AddComponentChunkOp
//    {
//        Chunks = batch.ActiveFullArchetypeChunks.Ptr,
//        TypeIndex = data.DisabledTypeIndex,
//        Count = remainingChunks * -1
//    });

//    batch.RequiresActiveUpdate = true;
//    batch.RequiresInactiveUpdate = true;
//}

//if ()
//{
//    data.AddComponentToChunks.Add(new AddComponentChunkOp
//    {
//        Chunks = batch.ActiveFull.Ptr,
//        TypeIndex = data.DisabledTypeIndex,
//        Count = excessChunks * -1
//    });
//}


//if (batch.ActivePartial.Length > 0)
//{
//    // deactivate all

//    var excessChunks = batch.ActiveChunks.Length - requiredChunks;
//    if (excessChunks == 0)
//    {
//        //excess = 0 do nothing.

//        return;
//    }

//}

// >> convert all partials until there are none left
// >> 


//if (batch.ActiveChunks.Length > 1)
//if (batch.ActivePartialArchetypeChunkPtrs.Length > 0) // we only need 1 partial or active chunk.
//{
//    // destroy some actives.
//}

// Convert from partials first.

//if (batch.ActivePartialArchetypeChunkPtrs.Length == 1)
//{
//    // There is already an active chunk. try to keep it as-is or split it.

//    var archetypeChunkPtr = (ArchetypeChunk*)batch.ActivePartialArchetypeChunkPtrs.Ptr[0];
//    if (archetypeChunkPtr->Count == requiredEntities)
//    {
//        return;
//    }
//    else
//    {
//        // remember, single chunk
//        // Split off the excess.

//        var batchInChunks = new NativeList<EntityBatchInChunkProxy>(1, Allocator.Temp);
//        var amountToMove = archetypeChunkPtr->Count - requiredEntities; // since its 1 chunk this can't be negative.
//        var entityBatch = new EntityBatchInChunkProxy
//        {
//            ChunkPtr = archetypeChunkPtr->GetChunkPtr(),
//            Count = amountToMove,
//            StartIndex = requiredEntities,
//        };
//        batchInChunks.Add(entityBatch);
//        data.AddComponentBatches.Add(new AddComponentBatchOp
//        {
//            EntityBatches = batchInChunks.AsParallelWriter().ListData,
//            TypeIndex = data.DisabledTypeIndex,
//        });
//    }
//}
//else if (batch.ActivePartialArchetypeChunkPtrs.Length > 1)
//{
//    // keep active partials up to x and disable the rest.


//}