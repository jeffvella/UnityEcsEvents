using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using System.Runtime.CompilerServices;
using Unity.Collections;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Vella.Events.Extensions;

namespace Vella.Events
{

    public unsafe struct UnsafeEntityManager
    {
        private void* _componentDataStore;

        public UnsafeEntityManager(EntityManager em)
        {
            StructuralChangeProxy.Initialize();
            var entity = em.CreateEntity();
            var chunk = em.GetChunk(entity);
            _componentDataStore = chunk.GetComponentDataStorePtr();
            em.DestroyEntity(entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateEntity(EntityArchetype entityArchetype, NativeArray<Entity> destination)
        {
            StructuralChangeProxy.CreateEntity.Invoke(_componentDataStore, entityArchetype.GetArchetypePtr(), *(Entity**)UnsafeUtility.AddressOf(ref destination), destination.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateEntity(EntityArchetype entityArchetype, NativeArray<Entity> destination, int count, int startOffset = 0)
        {
            var ptr = (Entity*)((*(byte**)UnsafeUtility.AddressOf(ref destination)) + sizeof(Entity) * startOffset);
            
            StructuralChangeProxy.CreateEntity.Invoke(_componentDataStore, entityArchetype.GetArchetypePtr(), ptr, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateEntity(EntityArchetype entityArchetype, void* destination, int entityCount, int startOffset = 0)
        {
            StructuralChangeProxy.CreateEntity(_componentDataStore, entityArchetype.GetArchetypePtr(), (Entity*)((byte*)destination + sizeof(Entity) * startOffset), entityCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateEntity(void* entityArchetypePtr, void* destination, int entityCount, int startOffset = 0)
        {
            StructuralChangeProxy.CreateEntity(_componentDataStore, entityArchetypePtr, (Entity*)((byte*)destination + sizeof(Entity) * startOffset), entityCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetArchetype(EntityArchetype destinationArchetype, void* entities, int length)
        {
            var archetypePtr = destinationArchetype.GetArchetypePtr();
            for (int i = 0; i < length; i++)
            {
                StructuralChangeProxy.MoveEntityArchetype.Invoke(_componentDataStore, (Entity*)((byte*)entities + i * sizeof(Entity)), archetypePtr);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MoveEntityArchetype(EntityArchetype destinationArchetype, Entity entity, int length)
        {
            StructuralChangeProxy.MoveEntityArchetype.Invoke(_componentDataStore, &entity, destinationArchetype.GetArchetypePtr());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponentToChunks(ArchetypeChunk* chunks, int chunkCount, int typeIndex)
        {
            StructuralChangeProxy.AddComponentChunks(_componentDataStore, chunks, chunkCount, typeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void MoveEntityArchetype(Entity entity, EntityArchetype destinationArchetype)
        {
            StructuralChangeProxy.MoveEntityArchetype.Invoke(_componentDataStore, &entity, destinationArchetype.GetArchetypePtr());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponentToChunks(void* chunks, int chunkCount, int typeIndex)
        {
            StructuralChangeProxy.AddComponentChunks.Invoke(_componentDataStore, (ArchetypeChunk*)chunks, chunkCount, typeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponentFromChunks(ArchetypeChunk* chunks, int chunkCount, int typeIndex)
        {
            StructuralChangeProxy.RemoveComponentChunks(_componentDataStore, chunks, chunkCount, typeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponentFromChunks(void* chunks, int chunkCount, int typeIndex)
        {
            StructuralChangeProxy.RemoveComponentChunks(_componentDataStore, (ArchetypeChunk*)chunks, chunkCount, typeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponentEntitiesBatch(UnsafeList* batchInChunkList, int typeIndex)
        {
            StructuralChangeProxy.AddComponentEntitiesBatch.Invoke(_componentDataStore, batchInChunkList, typeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponentEntitiesBatch(UnsafeList* batchInChunkList, int typeIndex)
        {
            StructuralChangeProxy.RemoveComponentEntitiesBatch.Invoke(_componentDataStore, batchInChunkList, typeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddComponentEntity(Entity* entity, int typeIndex)
        {
            StructuralChangeProxy.AddComponentEntity.Invoke(_componentDataStore, entity, typeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveComponentEntity(Entity* entity, int typeIndex)
        {
            StructuralChangeProxy.RemoveComponentEntity.Invoke(_componentDataStore, entity, typeIndex);
        }

        [BurstCompile]
        internal unsafe struct StructuralChangeProxy
        {
            public delegate void AddComponentEntitiesBatchDelegate(void* entityComponentStore, UnsafeList* entityBatchList, int typeIndex);
            public delegate bool AddComponentEntityDelegate(void* entityComponentStore, Entity* entity, int typeIndex);
            public delegate void AddComponentChunksDelegate(void* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int typeIndex);
            public delegate bool RemoveComponentEntityDelegate(void* entityComponentStore, Entity* entity, int typeIndex);
            public delegate void RemoveComponentEntitiesBatchDelegate(void* entityComponentStore, UnsafeList* entityBatchList, int typeIndex);
            public delegate void RemoveComponentChunksDelegate(void* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int typeIndex);
            public delegate void AddSharedComponentChunksDelegate(void* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, int componentTypeIndex, int sharedComponentIndex);
            public delegate void SetChunkComponentDelegate(void* entityComponentStore, ArchetypeChunk* chunks, int chunkCount, void* componentData, int componentTypeIndex);
            public delegate void InstantiateEntitiesDelegate(void* entityComponentStore, Entity* srcEntity, Entity* outputEntities, int instanceCount);
            public delegate void MoveEntityArchetypeDelegate(void* entityComponentStore, Entity* entity, void* dstArchetype);
            public delegate void CreateEntityDelegate(void* entityComponentStore, void* archetype, Entity* outEntities, int count);

            public static AddComponentEntitiesBatchDelegate AddComponentEntitiesBatch;
            public static AddComponentEntityDelegate AddComponentEntity;
            public static AddComponentChunksDelegate AddComponentChunks;
            public static RemoveComponentEntityDelegate RemoveComponentEntity;
            public static RemoveComponentEntitiesBatchDelegate RemoveComponentEntitiesBatch;
            public static RemoveComponentChunksDelegate RemoveComponentChunks;
            public static AddSharedComponentChunksDelegate AddSharedComponentChunks;
            public static MoveEntityArchetypeDelegate MoveEntityArchetype;
            public static SetChunkComponentDelegate SetChunkComponent;
            public static CreateEntityDelegate CreateEntity;
            public static InstantiateEntitiesDelegate InstantiateEntities;

            public static void Initialize()
            {
                if (AddComponentEntitiesBatch != null)
                    return;

                var StructuralChangeType = ReflectionHelper.FindType("Unity.Entities.StructuralChange");
                if (StructuralChangeType == null)
                    throw new TypeLoadException();

                AddComponentEntitiesBatch = ExtractDelegate<AddComponentEntitiesBatchDelegate>(StructuralChangeType, nameof(AddComponentEntitiesBatch)).Delegate;
                AddComponentEntity = ExtractDelegate<AddComponentEntityDelegate>(StructuralChangeType, nameof(AddComponentEntity)).Delegate;
                AddComponentChunks = ExtractDelegate<AddComponentChunksDelegate>(StructuralChangeType, nameof(AddComponentChunks)).Delegate;
                RemoveComponentEntity = ExtractDelegate<RemoveComponentEntityDelegate>(StructuralChangeType, nameof(RemoveComponentEntity)).Delegate;
                RemoveComponentEntitiesBatch = ExtractDelegate<RemoveComponentEntitiesBatchDelegate>(StructuralChangeType, nameof(RemoveComponentEntitiesBatch)).Delegate;
                RemoveComponentChunks = ExtractDelegate<RemoveComponentChunksDelegate>(StructuralChangeType, nameof(RemoveComponentChunks)).Delegate;
                AddSharedComponentChunks = ExtractDelegate<AddSharedComponentChunksDelegate>(StructuralChangeType, nameof(AddSharedComponentChunks)).Delegate;
                MoveEntityArchetype = ExtractDelegate<MoveEntityArchetypeDelegate>(StructuralChangeType, nameof(MoveEntityArchetype)).Delegate;
                SetChunkComponent = ExtractDelegate<SetChunkComponentDelegate>(StructuralChangeType, nameof(SetChunkComponent)).Delegate;
                CreateEntity = ExtractDelegate<CreateEntityDelegate>(StructuralChangeType, nameof(CreateEntity)).Delegate;
                InstantiateEntities = ExtractDelegate<InstantiateEntitiesDelegate>(StructuralChangeType, nameof(InstantiateEntities)).Delegate;
            }

            private static (T Delegate, IntPtr Pointer) ExtractDelegate<T>(Type StructuralChangeType, string fieldName) where T : Delegate
            {
                StaticRouter<Delegate> r = new StaticRouter<Delegate>(StructuralChangeType, fieldName);

                // Unity's delegates require an inaccessible pointer argument type, these delegates replace it with void*
                Delegate privateTypeParamDelegate = r.Value.GetInvocationList()[0];
                IntPtr ptr = Marshal.GetFunctionPointerForDelegate(privateTypeParamDelegate);
                var castDelegate = Unsafe.As<T>(privateTypeParamDelegate);

                return (castDelegate, ptr);
            }
        }

        internal class StaticRouter<T>
        {
            public T Value => _expr();
            private readonly Func<T> _expr;

            public StaticRouter(Type parentType, string memberName)
            {
                _expr = ReflectionHelper.GetStaticAccessor<T>(parentType, memberName);
            }
        }

        internal static class ReflectionHelper
        {
            public static Type FindType(string fullName)
            {
                return
                    AppDomain.CurrentDomain.GetAssemblies()
                        .Where(a => !a.IsDynamic)
                        .SelectMany(a => a.GetTypes())
                        .FirstOrDefault(t => t.FullName.Equals(fullName));
            }

            public static Func<T> GetStaticAccessor<T>(Type containingClassType, string memberName)
            {
                var member = GetStaticPropertyOrField(containingClassType, memberName);
                var lambda = Expression.Lambda(member);
                return (Func<T>)lambda.Compile();
            }

            public static MemberExpression GetStaticPropertyOrField(Type type, string propertyOrFieldName)
            {
                if (type == null)
                    throw new ArgumentNullException(nameof(type));

                PropertyInfo property = type.GetProperty(propertyOrFieldName, BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Static);
                if (property != null)
                {
                    return Expression.Property(null, property);
                }
                FieldInfo field = type.GetField(propertyOrFieldName, BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Static);
                if (field == null)
                {
                    property = type.GetProperty(propertyOrFieldName, BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.Static);
                    if (property != null)
                    {
                        return Expression.Property(null, property);
                    }
                    field = type.GetField(propertyOrFieldName, BindingFlags.FlattenHierarchy | BindingFlags.NonPublic | BindingFlags.IgnoreCase | BindingFlags.Static);
                    if (field == null)
                    {
                        throw new ArgumentException($"{propertyOrFieldName} NotAMemberOfType {type}");
                    }
                }
                return Expression.Field(null, field);
            }
        }

    }



}


