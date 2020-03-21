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
        public void CreateEntities(EntityArchetype entityArchetype, NativeArray<Entity> destination)
        {
            StructuralChangeProxy.CreateEntity.Invoke(_componentDataStore, entityArchetype.GetArchetypePtr(), *(Entity**)UnsafeUtility.AddressOf(ref destination), destination.Length);
        }

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public void CreateEntity(EntityArchetype entityArchetype, NativeArray<Entity> destination, int count, int startOffset = 0)
        //{
        //    var ptr = (Entity*)((*(byte**)UnsafeUtility.AddressOf(ref destination)) + sizeof(Entity) * startOffset);
        //    StructuralChangeProxy.CreateEntity(_componentDataStore, entityArchetype.GetArchetypePtr(), ptr, count);
        //}

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateEntity(EntityArchetype entityArchetype, NativeArray<Entity> destination, int count, int startOffset = 0)
        {
            var ptr = (Entity*)((*(byte**)UnsafeUtility.AddressOf(ref destination)) + sizeof(Entity) * startOffset);
            
            StructuralChangeProxy.CreateEntity(_componentDataStore, entityArchetype.GetArchetypePtr(), ptr, count);

            //StructuralChangeProxy.TestSharedData.Shared.Data.CreateEntity.Invoke(_componentDataStore, entityArchetype.GetArchetypePtr(), ptr, count);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateEntity(EntityArchetype entityArchetype, void* destination, int entityCount, int startOffset = 0)
        {
            StructuralChangeProxy.CreateEntity.Invoke(_componentDataStore, entityArchetype.GetArchetypePtr(), (Entity*)((byte*)destination + sizeof(Entity) * startOffset), entityCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreateEntity(void* entityArchetypePtr, void* destination, int entityCount, int startOffset = 0)
        {
            StructuralChangeProxy.CreateEntity.Invoke(_componentDataStore, entityArchetypePtr, (Entity*)((byte*)destination + sizeof(Entity) * startOffset), entityCount);
        }
    }



    [BurstCompile]
    public unsafe struct StructuralChangeProxy
    {
        //public static class TestSharedData
        //{
        //    public static readonly SharedStatic<Delegates> Shared;

        //    private class Key { }

        //    static TestSharedData()
        //    {
        //        Shared = SharedStatic<Delegates>.GetOrCreate<Key>();
        //    }

        //    public struct Delegates
        //    {
        //        public FunctionPointer<AddComponentEntitiesBatchDelegate> AddComponentEntitiesBatch;
        //        public FunctionPointer<AddComponentEntityDelegate> AddComponentEntity;
        //        public FunctionPointer<AddComponentChunksDelegate> AddComponentChunks;
        //        public FunctionPointer<RemoveComponentEntityDelegate> RemoveComponentEntity;
        //        public FunctionPointer<RemoveComponentEntitiesBatchDelegate> RemoveComponentEntitiesBatch;
        //        public FunctionPointer<RemoveComponentChunksDelegate> RemoveComponentChunks;
        //        public FunctionPointer<AddSharedComponentChunksDelegate> AddSharedComponentChunks;
        //        public FunctionPointer<MoveEntityArchetypeDelegate> MoveEntityArchetype;
        //        public FunctionPointer<SetChunkComponentDelegate> SetChunkComponent;
        //        public FunctionPointer<CreateEntityDelegate> CreateEntity;
        //        public FunctionPointer<InstantiateEntitiesDelegate> InstantiateEntities;
        //    }
        //}

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

            AddComponentEntitiesBatch = UnsafeExtractDelegate<AddComponentEntitiesBatchDelegate>(StructuralChangeType, nameof(AddComponentEntitiesBatch)).Delegate;
            AddComponentEntity = UnsafeExtractDelegate<AddComponentEntityDelegate>(StructuralChangeType, nameof(AddComponentEntity)).Delegate;
            AddComponentChunks = UnsafeExtractDelegate<AddComponentChunksDelegate>(StructuralChangeType, nameof(AddComponentChunks)).Delegate;
            RemoveComponentEntity = UnsafeExtractDelegate<RemoveComponentEntityDelegate>(StructuralChangeType, nameof(RemoveComponentEntity)).Delegate;
            RemoveComponentEntitiesBatch = UnsafeExtractDelegate<RemoveComponentEntitiesBatchDelegate>(StructuralChangeType, nameof(RemoveComponentEntitiesBatch)).Delegate;
            RemoveComponentChunks = UnsafeExtractDelegate<RemoveComponentChunksDelegate>(StructuralChangeType, nameof(RemoveComponentChunks)).Delegate;
            AddSharedComponentChunks = UnsafeExtractDelegate<AddSharedComponentChunksDelegate>(StructuralChangeType, nameof(AddSharedComponentChunks)).Delegate;
            MoveEntityArchetype = UnsafeExtractDelegate<MoveEntityArchetypeDelegate>(StructuralChangeType, nameof(MoveEntityArchetype)).Delegate;
            SetChunkComponent = UnsafeExtractDelegate<SetChunkComponentDelegate>(StructuralChangeType, nameof(SetChunkComponent)).Delegate;
            CreateEntity = UnsafeExtractDelegate<CreateEntityDelegate>(StructuralChangeType, nameof(CreateEntity)).Delegate;
            InstantiateEntities = UnsafeExtractDelegate<InstantiateEntitiesDelegate>(StructuralChangeType, nameof(InstantiateEntities)).Delegate;

            //TestSharedData.Shared.Data = new TestSharedData.Delegates
            //{
            //    //AddComponentEntitiesBatch = new FunctionPointer<AddComponentEntitiesBatchDelegate>(UnsafeExtractDelegate<AddComponentEntitiesBatchDelegate>(StructuralChangeType, nameof(AddComponentEntitiesBatch)).Pointer),
            //    //AddComponentEntity = new FunctionPointer<AddComponentEntityDelegate>(UnsafeExtractDelegate<AddComponentEntityDelegate>(StructuralChangeType, nameof(AddComponentEntity)).Pointer),
            //    //AddComponentChunks = new FunctionPointer<AddComponentChunksDelegate>(UnsafeExtractDelegate<AddComponentChunksDelegate>(StructuralChangeType, nameof(AddComponentChunks)).Pointer),
            //    //RemoveComponentEntity = new FunctionPointer<RemoveComponentEntityDelegate>(UnsafeExtractDelegate<RemoveComponentEntityDelegate>(StructuralChangeType, nameof(RemoveComponentEntity)).Pointer),
            //    //RemoveComponentEntitiesBatch = new FunctionPointer<RemoveComponentEntitiesBatchDelegate>(UnsafeExtractDelegate<RemoveComponentEntitiesBatchDelegate>(StructuralChangeType, nameof(RemoveComponentEntitiesBatch)).Pointer),
            //    //RemoveComponentChunks = new FunctionPointer<RemoveComponentChunksDelegate>(UnsafeExtractDelegate<RemoveComponentChunksDelegate>(StructuralChangeType, nameof(RemoveComponentChunks)).Pointer),
            //    //AddSharedComponentChunks = new FunctionPointer<AddSharedComponentChunksDelegate>(UnsafeExtractDelegate<AddSharedComponentChunksDelegate>(StructuralChangeType, nameof(AddSharedComponentChunks)).Pointer),
            //    //MoveEntityArchetype = new FunctionPointer<MoveEntityArchetypeDelegate>(UnsafeExtractDelegate<MoveEntityArchetypeDelegate>(StructuralChangeType, nameof(MoveEntityArchetype)).Pointer),
            //    //SetChunkComponent = new FunctionPointer<SetChunkComponentDelegate>(UnsafeExtractDelegate<SetChunkComponentDelegate>(StructuralChangeType, nameof(SetChunkComponent)).Pointer),
            //    CreateEntity = new FunctionPointer<CreateEntityDelegate>(Marshal.GetFunctionPointerForDelegate(CreateEntity)),
            //    //InstantiateEntities = new FunctionPointer<InstantiateEntitiesDelegate>(UnsafeExtractDelegate<InstantiateEntitiesDelegate>(StructuralChangeType, nameof(InstantiateEntities)).Pointer),
            //};

        }

        private static (T Delegate, IntPtr Pointer) UnsafeExtractDelegate<T>(Type StructuralChangeType, string fieldName) where T : Delegate
        {
            StaticRouter<Delegate> r = new StaticRouter<Delegate>(StructuralChangeType, fieldName);
            Delegate privateTypeParamDelegate = r.Value.GetInvocationList()[0];
            IntPtr ptr = Marshal.GetFunctionPointerForDelegate(privateTypeParamDelegate);
            var castDelegate = Unsafe.As<T>(privateTypeParamDelegate);
            return (castDelegate, ptr);
        }
    }

    //public readonly struct WildFunctionPointer<T> : IFunctionPointer
    //{
    //    [NativeDisableUnsafePtrRestriction]
    //    private readonly System.IntPtr _ptr;

    //    public System.IntPtr Value => _ptr;

    //    public T Invoke
    //    {
    //        get
    //        {
    //            return (T)(object)Marshal.GetDelegateForFunctionPointer(_ptr, typeof(T));
    //        }
    //    }

    //    public bool IsCreated => _ptr != IntPtr.Zero;

    //    public WildFunctionPointer(System.IntPtr ptr)
    //    {
    //        _ptr = ptr;
    //    }

    //    IFunctionPointer IFunctionPointer.FromIntPtr(System.IntPtr ptr)
    //    {
    //        return new FunctionPointer<T>(ptr);
    //    }
    //}

    public class StaticRouter<T>
    {
        public T Value => _expr();
        private readonly Func<T> _expr;

        public StaticRouter(Type parentType, string memberName)
        {
            _expr = ReflectionHelper.GetStaticAccessor<T>(parentType, memberName);
        }
    }

    public static class ReflectionHelper
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


