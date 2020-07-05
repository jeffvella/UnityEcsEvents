using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Unity.Entities
{
    public unsafe partial struct EntityManager
    {
        public UnsafeEntityManager Unsafe => new UnsafeEntityManager(this.m_EntityDataAccess->EntityComponentStore);
        
        public void BeforeStructuralChange_() => BeforeStructuralChange();
    }
}

public unsafe struct UnsafeEntityManager
{
    private readonly EntityComponentStore* _entityComponentStore;

    internal UnsafeEntityManager(EntityComponentStore* entityComponentStore) => this._entityComponentStore = entityComponentStore;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* GetComponentPtr<T>(Entity entity, int typeIndex) where T : unmanaged
    {
        return (T*) _entityComponentStore->GetComponentDataRawRWEntityHasComponent(entity, typeIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* GetComponentPtr<T>(Entity entity) where T : unmanaged
    {
        return (T*) _entityComponentStore->GetComponentDataRawRWEntityHasComponent(entity, TypeManager.GetTypeIndex<T>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void* GetComponentPtr(Entity entity, int typeIndex)
    {
        return _entityComponentStore->GetComponentDataRawRWEntityHasComponent(entity, typeIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte* GetComponentPtr(ArchetypeChunk archetypeChunk, int componentTypeIndex, int entityIndexInChunk = 0)
    {
        var typeIndexInChunk = ChunkDataUtility.GetIndexInTypeArray(archetypeChunk.m_Chunk->Archetype, componentTypeIndex);
        return ChunkDataUtility.GetComponentDataRO(archetypeChunk.m_Chunk, entityIndexInChunk, typeIndexInChunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte* GetComponentPtr(void* chunkPtr, int componentTypeIndex, int entityIndexInChunk = 0)
    {
        var chunk = (Chunk*) chunkPtr;
        var typeIndexInChunk = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, componentTypeIndex);
        return ChunkDataUtility.GetComponentDataRO(chunk, entityIndexInChunk, typeIndexInChunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* GetComponentPtr<T>(ArchetypeChunk archetypeChunk, int componentTypeIndex, int entityIndexInChunk) where T : unmanaged, IComponentData
    {
        var typeIndexInChunk = ChunkDataUtility.GetIndexInTypeArray(archetypeChunk.m_Chunk->Archetype, componentTypeIndex);
        return (T*) ChunkDataUtility.GetComponentDataRO(archetypeChunk.m_Chunk, entityIndexInChunk, typeIndexInChunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* GetComponentPtr<T>(void* chunkPtr, int componentTypeIndex, int entityIndexInChunk = 0) where T : unmanaged, IComponentData
    {
        Chunk* chunk = (Chunk*) chunkPtr;
        var typeIndexInChunk = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, componentTypeIndex);
        return (T*) ChunkDataUtility.GetComponentDataRO(chunk, entityIndexInChunk, typeIndexInChunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* GetComponentPtr<T>(ArchetypeChunk archetypeChunk, int entityIndexInChunk = 0) where T : unmanaged, IComponentData
    {
        return GetComponentPtr<T>(archetypeChunk, TypeManager.GetTypeIndex<T>(), entityIndexInChunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* GetComponentPtr<T>(byte* chunkPtr, int entityIndexInChunk = 0) where T : unmanaged, IComponentData
    {
        return GetComponentPtr<T>(chunkPtr, TypeManager.GetTypeIndex<T>(), entityIndexInChunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetChangeVersion(ArchetypeChunk archetypeChunk, int componentTypeIndex)
        => archetypeChunk.m_Chunk->SetChangeVersion(componentTypeIndex, _entityComponentStore->GlobalSystemVersion);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetChangeVersion(ArchetypeChunk archetypeChunk, int typeIndex)
        => archetypeChunk.m_Chunk->GetChangeVersion(typeIndex);

    public uint GlobalSystemVersion => _entityComponentStore->GlobalSystemVersion;
    public int EntitiesCapacity => _entityComponentStore->EntitiesCapacity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Exists(Entity entity) => _entityComponentStore->Exists(entity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte* GetChunkPtr(Entity entity) => (byte*) _entityComponentStore->GetEntityInChunk(entity).Chunk;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte* GetChunkPtr(ArchetypeChunk chunk) => (byte*) chunk.m_Chunk;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte* GetArchetypePtr(Entity entity) => (byte*) _entityComponentStore->GetArchetype(entity);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetIndexInChunk(Entity entity) => _entityComponentStore->GetEntityInChunk(entity).IndexInChunk;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetEntityCount(Entity entity) => _entityComponentStore->GetArchetype(entity)->EntityCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetEntityCount(void* archetypePtr) => ((Archetype*) archetypePtr)->EntityCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetChunkCount(Entity entity) => _entityComponentStore->GetArchetype(entity)->Chunks.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetChunkCount(void* archetypePtr) => ((Archetype*) archetypePtr)->Chunks.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int* GetComponentOffsets(Entity entity) => _entityComponentStore->GetArchetype(entity)->Offsets;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int* GetComponentTypeIndexes(Entity entity) => (int*) _entityComponentStore->GetArchetype(entity)->Types;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetComponentTypeCount(Entity entity) => _entityComponentStore->GetArchetype(entity)->TypesCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetComponentTypeCount(void* archetypePtr) => ((Archetype*) archetypePtr)->TypesCount;

    #region StructuralChanges

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddComponentEntitiesBatch(UnsafeList* entityBatchList, int typeIndex)
    {
        _entityComponentStore->AddComponent(entityBatchList, ComponentType.FromTypeIndex(typeIndex), 0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AddComponentEntity(Entity* entity, int typeIndex)
    {
        return _entityComponentStore->AddComponent(*entity, ComponentType.FromTypeIndex(typeIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddComponentChunks(ArchetypeChunk* chunks, int chunkCount, int typeIndex)
    {
        _entityComponentStore->AddComponent(chunks, chunkCount, ComponentType.FromTypeIndex(typeIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool RemoveComponentEntity(Entity* entity, int typeIndex)
    {
        return _entityComponentStore->RemoveComponent(*entity, ComponentType.FromTypeIndex(typeIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveComponentEntitiesBatch(UnsafeList* entityBatchList, int typeIndex)
    {
        _entityComponentStore->RemoveComponent(entityBatchList, ComponentType.FromTypeIndex(typeIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RemoveComponentChunks(ArchetypeChunk* chunks, int chunkCount, int typeIndex)
    {
        _entityComponentStore->RemoveComponent(chunks, chunkCount, ComponentType.FromTypeIndex(typeIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddSharedComponentChunks(ArchetypeChunk* chunks, int chunkCount, int componentTypeIndex, int sharedComponentIndex)
    {
        _entityComponentStore->AddComponent(chunks, chunkCount, ComponentType.FromTypeIndex(componentTypeIndex), sharedComponentIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void MoveEntityArchetype(Entity* entity, void* dstArchetype)
    {
        _entityComponentStore->Move(*entity, (Archetype*) dstArchetype);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetChunkComponent(ArchetypeChunk* chunks, int chunkCount, void* componentData, int componentTypeIndex)
    {
        _entityComponentStore->SetChunkComponent(chunks, chunkCount, componentData, componentTypeIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CreateEntity(void* archetype, Entity* outEntities, int count)
    {
        _entityComponentStore->CreateEntities((Archetype*) archetype, outEntities, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CreateEntity(EntityArchetype archetype, Entity* outEntities, int count)
    {
        CreateEntity(archetype.Archetype, outEntities, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CreateEntity(EntityArchetype archetype, NativeArray<Entity> entities)
    {
        CreateEntity(archetype, (Entity*) entities.GetUnsafePtr(), entities.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity CreateEntity(EntityArchetype archetype)
    {
        return _entityComponentStore->CreateEntityWithValidation(archetype);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CreateEntity(EntityArchetype entityArchetype, NativeArray<Entity> destination, int count, int startOffset = 0)
    {
        var ptr = (Entity*) (*(byte**) UnsafeUtility.AddressOf(ref destination) + sizeof(Entity) * startOffset);

        CreateEntity(entityArchetype.Archetype, ptr, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CreateEntity(EntityArchetype entityArchetype, void* destination, int entityCount, int startOffset = 0)
    {
        CreateEntity(entityArchetype.Archetype, (Entity*) ((byte*) destination + sizeof(Entity) * startOffset), entityCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CreateEntity(void* entityArchetypePtr, void* destination, int entityCount, int startOffset = 0)
    {
        CreateEntity(entityArchetypePtr, (Entity*) ((byte*) destination + sizeof(Entity) * startOffset), entityCount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetArchetype(EntityArchetype destinationArchetype, void* entities, int length)
    {
        for (int i = 0; i < length; i++)
        {
            MoveEntityArchetype((Entity*) ((byte*) entities + i * sizeof(Entity)), destinationArchetype.Archetype);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DestroyEntity(Entity entity)
    {
        DestroyEntity(&entity, 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void DestroyEntity(Entity* entities, int count)
    {
        _entityComponentStore->DestroyEntities(entities, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InstantiateEntities(Entity* srcEntity, Entity* outputEntities, int instanceCount)
    {
        _entityComponentStore->InstantiateEntities(*srcEntity, outputEntities, instanceCount);
    }

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetBufferRaw(Entity entity, int componentTypeIndex, void* bufferHeaderPtr, int sizeInChunk)
    {
        _entityComponentStore->SetBufferRawWithValidation(entity, componentTypeIndex, (BufferHeader*) bufferHeaderPtr, sizeInChunk);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void SetComponentDataRaw(Entity entity, int typeIndex, void* data, int size)
    {
        _entityComponentStore->SetComponentDataRawEntityHasComponent(entity, typeIndex, data, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComponentData<T>(Entity entity, int targetTypeIndex, T inputData) where T : struct, IComponentData
    {
        _entityComponentStore->AssertEntityHasComponent(entity, targetTypeIndex);
        var ptr = _entityComponentStore->GetComponentDataWithTypeRW(entity, targetTypeIndex, _entityComponentStore->GlobalSystemVersion);
        UnsafeUtility.CopyStructureToPtr(ref inputData, ptr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetComponentData<T>(Entity entity, ComponentType targetType, T inputData) where T : struct, IComponentData
    {
        SetComponentData(entity, targetType.TypeIndex, inputData);
    }

    public bool HasComponent(Entity entity, ComponentType type)
    {
        return _entityComponentStore->HasComponent(entity, type);
    }

    public bool HasComponent(Entity entity, int componentTypeIndex)
    {
        return _entityComponentStore->HasComponent(entity, componentTypeIndex);
    }

    public bool HasComponent(ArchetypeChunk archetypeChunk, int componentTypeIndex)
    {
        var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(archetypeChunk.m_Chunk->Archetype, componentTypeIndex);
        return typeIndexInArchetype != -1;
    }

    public bool HasComponent(void* chunkPtr, int componentTypeIndex)
    {
        var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(((Chunk*) chunkPtr)->Archetype, componentTypeIndex);
        return typeIndexInArchetype != -1;
    }
}

public static unsafe class UnsafeEntityManagerExtensions
{
    public static void* GetChunkPtr(this ArchetypeChunk archetypeChunk) => archetypeChunk.m_Chunk;

    public static void* GetArchetypePtr(this ArchetypeChunk archetypeChunk) => archetypeChunk.Archetype.Archetype;

    public static void* GetArchetypePtr(this EntityArchetype entityArchetype) => entityArchetype.Archetype;

    public static void* GetChunkPtr(this EntityArchetype entityArchetype, int index) => entityArchetype.Archetype->Chunks.p[index];
    
    public static ArchetypeChunk GetArchetypeChunk(this EntityArchetype entityArchetype, int index)
    {
        ArchetypeChunk result = default;
        result.m_Chunk = entityArchetype.Archetype->Chunks.p[index];
#if ENABLE_UNITY_COLLECTIONS_CHECKS        
        result.m_EntityComponentStore = entityArchetype._DebugComponentStore;
#endif        
        return result;
    }
}
