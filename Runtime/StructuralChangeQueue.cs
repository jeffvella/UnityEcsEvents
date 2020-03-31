using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Collections;

namespace Vella.Events
{
    public unsafe struct StructuralChangeQueue
    {
        public UnsafeEntityManager _uem;
        public UnsafeList CreateChunks;
        public UnsafeList AddComponentToChunks;
        public UnsafeList RemoveComponentFromChunks;
        public UnsafeList AddComponentBatches;
        public UnsafeList RemoveComponentBatches;
        public UnsafeAppendBuffer ChunkScratch;
        public UnsafeAppendBuffer BatchScratch;

        public StructuralChangeQueue(UnsafeEntityManager uem, Allocator allocator)
        {
            _uem = uem;
            CreateChunks = new UnsafeList(allocator);
            AddComponentToChunks = new UnsafeList(allocator);
            RemoveComponentFromChunks = new UnsafeList(allocator);
            AddComponentBatches = new UnsafeList(allocator);
            RemoveComponentBatches = new UnsafeList(allocator);
            ChunkScratch = new UnsafeAppendBuffer(4096, 4, Allocator.Persistent);
            BatchScratch = new UnsafeAppendBuffer(4096, 4, Allocator.Persistent);
        }

        internal void Apply()
        {
            //EntityManager.CompleteAllJobs(); // Such safety, much wow.

            for (int i = 0; i < AddComponentToChunks.Length; i++)
            {
                var op = ((AddComponentChunkOp*)AddComponentToChunks.Ptr)[i];

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                for (int j = 0; j < op.Count; j++)
                    ((ArchetypeChunk*)op.Chunks)[j].Invalid();
#endif

                _uem.AddComponentToChunks(op.Chunks, op.Count, op.TypeIndex);
            }

            for (int i = 0; i < RemoveComponentFromChunks.Length; i++)
            {

                var op = ((RemoveComponentChunkOp*)RemoveComponentFromChunks.Ptr)[i];

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                for (int j = 0; j < op.Count; j++)
                    ((ArchetypeChunk*)op.Chunks)[j].Invalid();
#endif
                _uem.RemoveComponentFromChunks(op.Chunks, op.Count, op.TypeIndex);
            }

            for (int i = 0; i < CreateChunks.Length; i++)
            {
                var op = ((CreateChunksOp*)CreateChunks.Ptr)[i];
                ChunkScratch.ResizeUninitialized(op.EntityCount * sizeof(Entity));
                _uem.CreateEntity(op.Archetype, ChunkScratch.Ptr, op.EntityCount);
            }

            for (int i = 0; i < RemoveComponentBatches.Length; i++)
            {
                var op = ((RemoveComponentBatchOp*)RemoveComponentBatches.Ptr)[i];
                _uem.RemoveComponentEntitiesBatch(op.EntityBatches, op.TypeIndex);
            }

            for (int i = 0; i < AddComponentBatches.Length; i++)
            {
                var op = ((AddComponentBatchOp*)AddComponentBatches.Ptr)[i];
                _uem.AddComponentEntitiesBatch(op.EntityBatches, op.TypeIndex);
            }
        }

        public void Dispose()
        {
            CreateChunks.Dispose();
            AddComponentToChunks.Dispose();
            RemoveComponentFromChunks.Dispose();
            AddComponentBatches.Dispose();
            RemoveComponentBatches.Dispose();
            ChunkScratch.Dispose();
            BatchScratch.Dispose();
        }

        public void Clear()
        {
            CreateChunks.Clear();
            AddComponentToChunks.Clear();
            RemoveComponentFromChunks.Clear();
            AddComponentBatches.Clear();
            RemoveComponentBatches.Clear();
            ChunkScratch.Reset();
            BatchScratch.Reset();
        }

    }

    #region Operation Structs

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
        public unsafe UnsafeList* EntityBatches;
        public int TypeIndex;
    }

    public struct AddComponentBatchOp
    {
        public unsafe UnsafeList* EntityBatches;
        public int TypeIndex;
    }

    public struct CreateChunksOp
    {
        public EntityArchetype Archetype;
        public int EntityCount;
    }

    #endregion

}


