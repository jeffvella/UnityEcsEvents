//using System;
//using System.Linq;
//using System.Linq.Expressions;
//using System.Reflection;
//using System.Runtime.InteropServices;
//using Unity.Burst;
//using Unity.Collections.LowLevel.Unsafe;
//using Unity.Entities;
//using System.Runtime.CompilerServices;
//using Unity.Collections;
//using System.Diagnostics;
//using Debug = UnityEngine.Debug;
//using Vella.Events.Extensions;
//using System.Collections.Generic;
//using UnityEngine;

//namespace Vella.Events
//{
//    public unsafe struct UnsafeEntityManager
//    {
//        [NativeDisableUnsafePtrRestriction]
//        private UnsafeDataComponentStore* _componentDataStore;
//        private AtomicSafetyHandle _unsafeSafety;

//        public static void* GetEntityComponentStorePtr(EntityManager em)
//        {
//            return Pointer.Unbox(typeof(EntityManager).GetField("m_EntityComponentStore").GetValue(em));
//        }

//        public UnsafeEntityManager(EntityManager em)
//        {
//            StructuralChangeProxy.Initialize();
//            var entity = em.CreateEntity();
//            _componentDataStore = (UnsafeDataComponentStore*)em.GetChunk(entity).GetComponentDataStorePtr();
//            em.DestroyEntity(entity);
//            _unsafeSafety = AtomicSafetyHandle.Create();
//        }

//        public UnsafeEntityManager(ArchetypeChunk chunk)
//        {
//            StructuralChangeProxy.Initialize();
//            _componentDataStore = (UnsafeDataComponentStore*)chunk.GetComponentDataStorePtr();
//            _unsafeSafety = AtomicSafetyHandle.Create();
//        }

//        public void RefreshSafety()
//        {
//            _unsafeSafety = AtomicSafetyHandle.Create();
//        }

//        // CS0649: Field is never assigned to, and will always have its default value 
//#pragma warning disable CS0649

//        private struct UnsafeDataComponentStore // Entities 0.8
//        {
//            [NativeDisableUnsafePtrRestriction]
//            public unsafe int* m_VersionByEntity;

//            [NativeDisableUnsafePtrRestriction]
//            public unsafe ArchetypeProxy** m_ArchetypeByEntity;

//            [NativeDisableUnsafePtrRestriction]
//            public unsafe EntityInChunk* m_EntityInChunkByEntity;

//            public struct EntityInChunk
//            {
//                internal unsafe byte* Chunk;
//                internal int IndexInChunk;
//            }
//        }

//#pragma warning restore CS0649

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public byte* GetChunkPtr(Entity entity)
//        {
//            return _componentDataStore->m_EntityInChunkByEntity[entity.Index].Chunk;
//        }

//        public bool Exists(Entity entity)
//        {
//            bool versionMatches = _componentDataStore->m_VersionByEntity[entity.Index] == entity.Version;
//            bool hasChunk = _componentDataStore->m_EntityInChunkByEntity[entity.Index].Chunk != null;
//            return entity.Index >= 0 && versionMatches && hasChunk;
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public T* GetComponentPtr<T>(Entity entity) where T : unmanaged, IComponentData
//        {
//            return (T*)GetComponentPtr(GetChunkPtr(entity), TypeManager.GetTypeIndex<T>());
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public T* GetComponentPtr<T>(Entity entity, int typeIndex) where T : unmanaged, IComponentData
//        {
//            return (T*)GetComponentPtr(GetChunkPtr(entity), typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public byte* GetComponentPtr(Entity entity, int typeIndex)
//        {
//            return GetComponentPtr(GetChunkPtr(entity), typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public ref T GetComponentRef<T>(Entity entity) where T : unmanaged, IComponentData
//        {
//            return ref *(T*)GetComponentPtr(GetChunkPtr(entity), TypeManager.GetTypeIndex<T>());
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public ref T GetComponentRef<T>(Entity entity, int typeIndex) where T : unmanaged, IComponentData
//        {
//            return ref *(T*)GetComponentPtr(GetChunkPtr(entity), typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public T* GetComponentPtr<T>(ArchetypeChunk chunk) where T : unmanaged, IComponentData
//        {
//            return (T*)GetComponentPtr(chunk, TypeManager.GetTypeIndex<T>());
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public ref T GetComponentRef<T>(ArchetypeChunk chunk) where T : unmanaged, IComponentData
//        {
//            return ref *(T*)GetComponentPtr(chunk, TypeManager.GetTypeIndex<T>());
//        }

//        public byte* GetComponentPtr(ArchetypeChunk chunk, int typeIndex)
//        {
//            ArchetypeChunkComponentTypeProxy accessorProxy;
//            accessorProxy.m_TypeIndex = typeIndex;
//            accessorProxy.m_Safety = AtomicSafetyHandle.Create();
//            var accessor = UnsafeUtilityEx.AsRef<ArchetypeChunkComponentType<ChunkHeader>>(&accessorProxy);
//            return (byte*)chunk.GetNativeArray(accessor).GetUnsafeReadOnlyPtr();
//        }

//        public byte* GetComponentPtr(void* chunkPtr, int typeIndex)
//        {
//            ArchetypeChunkProxy chunkProxy;
//            chunkProxy.m_Chunk = chunkPtr;
//            var chunk = *(ArchetypeChunk*)&chunkProxy;
//            return GetComponentPtr(chunk, typeIndex);
//        }

//        public struct ArchetypeChunkComponentTypeProxy
//        {
//            public int m_TypeIndex;
//            public uint m_GlobalSystemVersion;
//            public bool m_IsReadOnly;
//            public bool m_IsZeroSized;
//            public int m_Length;
//            public int m_MinIndex;
//            public int m_MaxIndex;
//            public AtomicSafetyHandle m_Safety;
//        }

//        public EntityArchetype GetArchetype(Entity entity)
//        {
//            EntityArchetypeProxy archetype;
//            archetype.Archetype = _componentDataStore->m_ArchetypeByEntity[entity.Index];
//            archetype._DebugComponentStore = _componentDataStore;
//            return *(EntityArchetype*)&archetype;
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void CreateEntity(EntityArchetype entityArchetype, NativeArray<Entity> destination)
//        {
//            StructuralChangeProxy.CreateEntity.Invoke(_componentDataStore, entityArchetype.GetArchetypePtr(), *(Entity**)UnsafeUtility.AddressOf(ref destination), destination.Length);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void CreateEntity(EntityArchetype entityArchetype, NativeArray<Entity> destination, int count, int startOffset = 0)
//        {
//            var ptr = (Entity*)((*(byte**)UnsafeUtility.AddressOf(ref destination)) + sizeof(Entity) * startOffset);
            
//            StructuralChangeProxy.CreateEntity.Invoke(_componentDataStore, entityArchetype.GetArchetypePtr(), ptr, count);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void CreateEntity(EntityArchetype entityArchetype, void* destination, int entityCount, int startOffset = 0)
//        {
//            StructuralChangeProxy.CreateEntity(_componentDataStore, entityArchetype.GetArchetypePtr(), (Entity*)((byte*)destination + sizeof(Entity) * startOffset), entityCount);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void CreateEntity(void* entityArchetypePtr, void* destination, int entityCount, int startOffset = 0)
//        {
//            StructuralChangeProxy.CreateEntity(_componentDataStore, entityArchetypePtr, (Entity*)((byte*)destination + sizeof(Entity) * startOffset), entityCount);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void SetArchetype(EntityArchetype destinationArchetype, void* entities, int length)
//        {
//            var archetypePtr = destinationArchetype.GetArchetypePtr();
//            for (int i = 0; i < length; i++)
//            {
//                StructuralChangeProxy.MoveEntityArchetype.Invoke(_componentDataStore, (Entity*)((byte*)entities + i * sizeof(Entity)), archetypePtr);
//            }
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void MoveEntityArchetype(EntityArchetype destinationArchetype, Entity entity, int length)
//        {
//            StructuralChangeProxy.MoveEntityArchetype.Invoke(_componentDataStore, &entity, destinationArchetype.GetArchetypePtr());
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void AddComponentToChunks(ArchetypeChunk* chunks, int chunkCount, int typeIndex)
//        {
//            StructuralChangeProxy.AddComponentChunks(_componentDataStore, chunks, chunkCount, typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void MoveEntityArchetype(Entity entity, EntityArchetype destinationArchetype)
//        {
//            StructuralChangeProxy.MoveEntityArchetype.Invoke(_componentDataStore, &entity, destinationArchetype.GetArchetypePtr());
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void AddComponentToChunks(void* chunks, int chunkCount, int typeIndex)
//        {
//            StructuralChangeProxy.AddComponentChunks.Invoke(_componentDataStore, (ArchetypeChunk*)chunks, chunkCount, typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void RemoveComponentFromChunks(ArchetypeChunk* chunks, int chunkCount, int typeIndex)
//        {
//            StructuralChangeProxy.RemoveComponentChunks(_componentDataStore, chunks, chunkCount, typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void RemoveComponentFromChunks(void* chunks, int chunkCount, int typeIndex)
//        {
//            StructuralChangeProxy.RemoveComponentChunks(_componentDataStore, (ArchetypeChunk*)chunks, chunkCount, typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void AddComponentEntitiesBatch(UnsafeList* batchInChunkList, int typeIndex)
//        {
//            StructuralChangeProxy.AddComponentEntitiesBatch.Invoke(_componentDataStore, batchInChunkList, typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void RemoveComponentEntitiesBatch(UnsafeList* batchInChunkList, int typeIndex)
//        {
//            StructuralChangeProxy.RemoveComponentEntitiesBatch.Invoke(_componentDataStore, batchInChunkList, typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void AddComponentEntity(Entity* entity, int typeIndex)
//        {
//            StructuralChangeProxy.AddComponentEntity.Invoke(_componentDataStore, entity, typeIndex);
//        }

//        [MethodImpl(MethodImplOptions.AggressiveInlining)]
//        public void RemoveComponentEntity(Entity* entity, int typeIndex)
//        {
//            StructuralChangeProxy.RemoveComponentEntity.Invoke(_componentDataStore, entity, typeIndex);
//        }

//        [BurstCompile]
//        internal unsafe struct StructuralChangeProxy
//        {
//            public delegate void AddComponentEntitiesBatchDelegate(void* entityComponentStore, UnsafeList* entityBatchList, int typeIndex);
//            public delegate bool AddComponentEntityDelegate(void* entityComponentStore, Entity* entity, int typeIndex);
//            public delegate void AddComponentChunksDelegate(void* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int typeIndex);
//            public delegate bool RemoveComponentEntityDelegate(void* entityComponentStore, Entity* entity, int typeIndex);
//            public delegate void RemoveComponentEntitiesBatchDelegate(void* entityComponentStore, UnsafeList* entityBatchList, int typeIndex);
//            public delegate void RemoveComponentChunksDelegate(void* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int typeIndex);
//            public delegate void AddSharedComponentChunksDelegate(void* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int componentTypeIndex, int sharedComponentIndex);
//            public delegate void SetChunkComponentDelegate(void* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, void* componentData, int componentTypeIndex);
//            public delegate void InstantiateEntitiesDelegate(void* entityComponentStore, Entity* srcEntity, Entity* outputEntities, int instanceCount);
//            public delegate void MoveEntityArchetypeDelegate(void* entityComponentStore, Entity* entity, void* dstArchetype);
//            public delegate void CreateEntityDelegate(void* entityComponentStore, void* archetype, Entity* outEntities, int count);

//            public static AddComponentEntitiesBatchDelegate AddComponentEntitiesBatch;
//            public static AddComponentEntityDelegate AddComponentEntity;
//            public static AddComponentChunksDelegate AddComponentChunks;
//            public static RemoveComponentEntityDelegate RemoveComponentEntity;
//            public static RemoveComponentEntitiesBatchDelegate RemoveComponentEntitiesBatch;
//            public static RemoveComponentChunksDelegate RemoveComponentChunks;
//            public static AddSharedComponentChunksDelegate AddSharedComponentChunks;
//            public static MoveEntityArchetypeDelegate MoveEntityArchetype;
//            public static SetChunkComponentDelegate SetChunkComponent;
//            public static CreateEntityDelegate CreateEntity;
//            public static InstantiateEntitiesDelegate InstantiateEntities;

//            public static void Initialize()
//            {
//                if (AddComponentEntitiesBatch != null)
//                    return;

//                var StructuralChangeType = ReflectionHelper.FindType("Unity.Entities.StructuralChange");
//                if (StructuralChangeType == null)
//                    throw new TypeLoadException();

//                AddComponentEntitiesBatch = ExtractDelegate<AddComponentEntitiesBatchDelegate>(StructuralChangeType, nameof(AddComponentEntitiesBatch)).Delegate;
//                AddComponentEntity = ExtractDelegate<AddComponentEntityDelegate>(StructuralChangeType, nameof(AddComponentEntity)).Delegate;
//                AddComponentChunks = ExtractDelegate<AddComponentChunksDelegate>(StructuralChangeType, nameof(AddComponentChunks)).Delegate;
//                RemoveComponentEntity = ExtractDelegate<RemoveComponentEntityDelegate>(StructuralChangeType, nameof(RemoveComponentEntity)).Delegate;
//                RemoveComponentEntitiesBatch = ExtractDelegate<RemoveComponentEntitiesBatchDelegate>(StructuralChangeType, nameof(RemoveComponentEntitiesBatch)).Delegate;
//                RemoveComponentChunks = ExtractDelegate<RemoveComponentChunksDelegate>(StructuralChangeType, nameof(RemoveComponentChunks)).Delegate;
//                AddSharedComponentChunks = ExtractDelegate<AddSharedComponentChunksDelegate>(StructuralChangeType, nameof(AddSharedComponentChunks)).Delegate;
//                MoveEntityArchetype = ExtractDelegate<MoveEntityArchetypeDelegate>(StructuralChangeType, nameof(MoveEntityArchetype)).Delegate;
//                SetChunkComponent = ExtractDelegate<SetChunkComponentDelegate>(StructuralChangeType, nameof(SetChunkComponent)).Delegate;
//                CreateEntity = ExtractDelegate<CreateEntityDelegate>(StructuralChangeType, nameof(CreateEntity)).Delegate;
//                InstantiateEntities = ExtractDelegate<InstantiateEntitiesDelegate>(StructuralChangeType, nameof(InstantiateEntities)).Delegate;
//            }

//            private static (T Delegate, IntPtr Pointer) ExtractDelegate<T>(Type type, string fieldName) where T : Delegate
//            {
//                StaticRouter<Delegate> r = new StaticRouter<Delegate>(type, fieldName);

//                // Unity's delegates require an inaccessible pointer argument type; these delegates use void* instead
//                Delegate privateTypeParamDelegate = r.Value.GetInvocationList()[0];
//                IntPtr ptr = Marshal.GetFunctionPointerForDelegate(privateTypeParamDelegate);
//                var castDelegate = Unsafe.As<T>(privateTypeParamDelegate);

//                return (castDelegate, ptr);
//            }


//        }

//        internal class StaticRouter<T>
//        {
//            public T Value => _expr();
//            private readonly Func<T> _expr;

//            public StaticRouter(Type parentType, string memberName)
//            {
//                _expr = ReflectionHelper.GetStaticAccessor<T>(parentType, memberName);
//            }
//        }

//        internal static class ReflectionHelper
//        {
//            public static Type FindType(string fullName)
//            {
//                return
//                    AppDomain.CurrentDomain.GetAssemblies()
//                        .Where(a => !a.IsDynamic)
//                        .SelectMany(a => a.GetTypes())
//                        .FirstOrDefault(t => t.FullName.Equals(fullName));
//            }

//            public static Func<T> GetStaticAccessor<T>(Type containingClassType, string memberName)
//            {
//                var member = GetStaticPropertyOrField(containingClassType, memberName);
//                var lambda = Expression.Lambda(member);
//                return (Func<T>)lambda.Compile();
//            }

//            public static MemberExpression GetStaticPropertyOrField(Type type, string propertyOrFieldName)
//            {
//                if (type == null)
//                    throw new ArgumentNullException(nameof(type));

//                PropertyInfo property = type.GetProperty(propertyOrFieldName, BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Static);
//                if (property != null)
//                {
//                    return Expression.Property(null, property);
//                }
//                FieldInfo field = type.GetField(propertyOrFieldName, BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Static);
//                if (field == null)
//                {
//                    property = type.GetProperty(propertyOrFieldName, BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.Static);
//                    if (property != null)
//                    {
//                        return Expression.Property(null, property);
//                    }
//                    field = type.GetField(propertyOrFieldName, BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.Static);
//                    if (field == null)
//                    {
//                        throw new ArgumentException($"{propertyOrFieldName} NotAMemberOfType {type}");
//                    }
//                }
//                return Expression.Field(null, field);
//            }
//        }

//    }



//}


