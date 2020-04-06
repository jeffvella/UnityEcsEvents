using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Profiling;
using Vella.Events;
using Vella.Tests.Data;

namespace Vella.Tests.Fixtures
{
    [DisableAutoCreation]
    public class EmptySystem : SystemBase
    {
        new public EntityQuery GetEntityQuery(params EntityQueryDesc[] queriesDesc)
        {
            return base.GetEntityQuery(queriesDesc);
        }

        new public EntityQuery GetEntityQuery(params ComponentType[] componentTypes)
        {
            return base.GetEntityQuery(componentTypes);
        }

        new public EntityQuery GetEntityQuery(NativeArray<ComponentType> componentTypes)
        {
            return base.GetEntityQuery(componentTypes);
        }

        public void AddDependencies(params JobHandle[] handles)
        {
            var newDeps = JobHandle.CombineDependencies(new NativeArray<JobHandle>(handles, Allocator.Temp));
            Dependency = JobHandle.CombineDependencies(Dependency, newDeps);
        }

        public JobHandle Dependencies
        {
            get => base.Dependency;
        }

        protected override void OnUpdate()
        {

        }

        public unsafe int GetMainThreadIndex()
        {
            var result = -1;
            new GetThreadIndexJob
            {
                ThreadIndex = &result
            }.Run();
            return result;
        }

        public unsafe struct  GetThreadIndexJob : IJob
        {
            private int _test;

#pragma warning disable IDE0044, CS0649
            [NativeSetThreadIndex]
            private int _threadIndex;
#pragma warning restore IDE0044, CS0649

            [NativeDisableUnsafePtrRestriction]
            public int* ThreadIndex;

            public void Execute()
            {
                *ThreadIndex = _threadIndex; 
            }
        }
    }


    public abstract class ECSTestsFixture
    {
        protected World m_PreviousWorld;
        protected World World;
        protected EntityManager Manager;
        protected EntityManager.EntityManagerDebug m_ManagerDebug;
        protected EntityQuery EcsTestDataQuery;
        protected EntityEventSystem EventSystem;

        protected int StressTestEntityCount = 1000;

        [SetUp]
        public virtual void Setup()
        {
            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            World = World.DefaultGameObjectInjectionWorld = new World("Test World");
            Manager = World.EntityManager;
            m_ManagerDebug = new EntityManager.EntityManagerDebug(Manager);

            EcsTestDataQuery = Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());
            EventSystem = Manager.World.GetOrCreateSystem<EntityEventSystem>();
        }

        [TearDown]
        public virtual void TearDown()
        {
            if (Manager != null && Manager.IsCreated)
            {
                // Clean up systems before calling CheckInternalConsistency because we might have filters etc
                // holding on SharedComponentData making checks fail
                while (World.Systems.Count > 0)
                {
                    World.DestroySystem(World.Systems[0]);
                }

                m_ManagerDebug.CheckInternalConsistency();

                World.Dispose();
                World = null;

                World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
                m_PreviousWorld = null;
                Manager = null;

                EventSystem = null;
                EcsTestDataQuery = null;
            }
        }

        //protected World m_PreviousWorld;
        //protected World World;
        //protected EntityManager Manager;
        //protected EntityManager.EntityManagerDebug m_ManagerDebug;

        //protected EntityQuery EcsTestDataQuery;
        //protected EntityEventSystem EventSystem;

        //protected int StressTestEntityCount = 1000;

        //[SetUp]
        //public virtual void Setup()
        //{
        //    m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
        //    World = World.DefaultGameObjectInjectionWorld = new World("Test World");
        //    Manager = World.EntityManager;
        //    m_ManagerDebug = new EntityManager.EntityManagerDebug(Manager);

        //    EcsTestDataQuery = Manager.CreateEntityQuery(ComponentType.ReadWrite<EcsTestData>());
        //    EventSystem = Manager.World.GetOrCreateSystem<EntityEventSystem>();
        //}

        //[TearDown]
        //public virtual void TearDown()
        //{
        //    if (Manager != null && Manager.IsCreated)
        //    {
        //        Clean up systems before calling CheckInternalConsistency because we might have filters etc
        //         holding on SharedComponentData making checks fail
        //        while (World.Systems.ToArray().Length > 0)
        //        {
        //            World.DestroySystem(World.Systems.ToArray()[0]);
        //        }

        //        m_ManagerDebug.CheckInternalConsistency();

        //        World.Dispose();
        //        World = null;

        //        World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
        //        m_PreviousWorld = null;
        //        Manager = null;
        //    }
        //}

        public static EcsTestData EventComponentData { get; } = new EcsTestData
        {
            value = 42
        };

        public unsafe void AssertBytesAreEqual<T>(NativeArray<T> arr1, NativeArray<T> arr2) where T : struct
        {
            var ptr1 = arr1.GetUnsafePtr();
            var ptr2 = arr2.GetUnsafePtr();
            var size = arr1.Length * UnsafeUtility.SizeOf<T>();

            Assert.AreEqual(arr1.Length, arr2.Length);
            Assert.AreEqual(0, UnsafeUtility.MemCmp(ptr1, ptr2, size));
        }

        public unsafe void AssertBytesAreEqual<T>(void* a, T b) where T : unmanaged
        {
            Assert.AreEqual(0, UnsafeUtility.MemCmp(a, &b, sizeof(T)));
        }

        public void AssertDoesNotExist(Entity entity)
        {
            Assert.IsFalse(Manager.HasComponent<EcsTestData>(entity));
            Assert.IsFalse(Manager.HasComponent<EcsTestData2>(entity));
            Assert.IsFalse(Manager.HasComponent<EcsTestData3>(entity));
            Assert.IsFalse(Manager.Exists(entity));
        }

        public void AssertComponentData(Entity entity, int index)
        {
            Assert.IsTrue(Manager.HasComponent<EcsTestData>(entity));
            Assert.IsTrue(Manager.HasComponent<EcsTestData2>(entity));
            Assert.IsFalse(Manager.HasComponent<EcsTestData3>(entity));
            Assert.IsTrue(Manager.Exists(entity));

            Assert.AreEqual(-index, Manager.GetComponentData<EcsTestData2>(entity).value0);
            Assert.AreEqual(-index, Manager.GetComponentData<EcsTestData2>(entity).value1);
            Assert.AreEqual(index, Manager.GetComponentData<EcsTestData>(entity).value);
        }

        public Entity CreateEntityWithDefaultData(int index)
        {
            var entity = Manager.CreateEntity(typeof(EcsTestData), typeof(EcsTestData2));

            // HasComponent & Exists setup correctly
            Assert.IsTrue(Manager.HasComponent<EcsTestData>(entity));
            Assert.IsTrue(Manager.HasComponent<EcsTestData2>(entity));
            Assert.IsFalse(Manager.HasComponent<EcsTestData3>(entity));
            Assert.IsTrue(Manager.Exists(entity));

            // Create must initialize values to zero
            Assert.AreEqual(0, Manager.GetComponentData<EcsTestData2>(entity).value0);
            Assert.AreEqual(0, Manager.GetComponentData<EcsTestData2>(entity).value1);
            Assert.AreEqual(0, Manager.GetComponentData<EcsTestData>(entity).value);

            // Setup some non zero default values
            Manager.SetComponentData(entity, new EcsTestData2(-index));
            Manager.SetComponentData(entity, new EcsTestData(index));

            AssertComponentData(entity, index);

            return entity;
        }

        public static string Pluralize(int number, string token) => number > 1 ? token + 's' : token;

        public static Type FindType(string fullName)
        {
            return
                AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.FullName.Equals(fullName));
        }

        public unsafe void AssertInstanceBytesAreEqual<T>(Type a) where T : unmanaged
        {
            var unityInstance = Activator.CreateInstance(a);
            var handle = GCHandle.Alloc(unityInstance, GCHandleType.Pinned);
            AssertBytesAreEqual((void*)handle.AddrOfPinnedObject(), new T());
            handle.Free();
        }

        public static unsafe void AssertTypeSizesAreEqual<T>(Type type) where T : unmanaged
        {
            Assert.IsNotNull(type);
            Assert.AreEqual(Marshal.SizeOf(type), sizeof(T));
        }

        public static void AssertFieldsAreEqual<T>(Type a, FieldComparisonFlags flags = default)
        {
            AssertFieldsAreEqual(a, typeof(T), flags);
        }

        [Flags]
        public enum FieldComparisonFlags
        {
            None = 0,
            AllowVoidPointerReplacement = 1,
            IgnorePointers = 2,
        }

        public static void AssertFieldsAreEqual(Type a, Type b, FieldComparisonFlags comparisonFlags)
        {
            const BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var aFields = a.GetFields(bindingFlags);
            var bFields = b.GetFields(bindingFlags);

            Assert.AreEqual(aFields.Length, bFields.Length);

            for (int i = 0; i < aFields.Length; i++)
            {
                var aField = aFields[i];
                var bField = bFields[i];

                var areSameType = aField.FieldType == bField.FieldType;
                if (!areSameType)
                {
                    var isVoidReplacement = ((comparisonFlags & FieldComparisonFlags.AllowVoidPointerReplacement) != 0) && (aField.FieldType == typeof(void*) || bField.FieldType == typeof(void*));
                    var isIgnoredPointerField = ((comparisonFlags & FieldComparisonFlags.IgnorePointers) != 0) && aField.FieldType.IsPointer && bField.FieldType.IsPointer;

                    if (!isVoidReplacement && !isIgnoredPointerField)
                    {
                        Assert.AreEqual(aField.FieldType, bField.FieldType);
                    }
                }

                if (aField.IsLiteral)
                    Assert.AreEqual(aField.GetRawConstantValue(), bField.GetRawConstantValue());
                else
                    AssertFieldOffsetsAreEqual(a, b, aField.Name);
            }
        }

        public static unsafe void AssertFieldOffsetsAreEqual(Type a, Type b, string fieldName)
        {
            var aOffset = Marshal.OffsetOf(a, fieldName);
            var bOffset = Marshal.OffsetOf(b, fieldName);
            Assert.AreEqual(aOffset, bOffset);
        }

        public void AssertSameChunk(Entity e0, Entity e1)
        {
            Assert.AreEqual(Manager.GetChunk(e0), Manager.GetChunk(e1));
        }

        public void AssertHasVersion<T>(Entity e, uint version) where T :
#if UNITY_DISABLE_MANAGED_COMPONENTS
            struct, 
#endif
            IComponentData
        {
            var type = Manager.GetArchetypeChunkComponentType<T>(true);
            var chunk = Manager.GetChunk(e);
            Assert.AreEqual(version, chunk.GetComponentVersion(type));
            Assert.IsFalse(chunk.DidChange(type, version));
            Assert.IsTrue(chunk.DidChange(type, version-1));
        }
        
        public void AssertHasBufferVersion<T>(Entity e, uint version) where T : struct, IBufferElementData
        {
            var type = Manager.GetArchetypeChunkBufferType<T>(true);
            var chunk = Manager.GetChunk(e);
            Assert.AreEqual(version, chunk.GetComponentVersion(type));
            Assert.IsFalse(chunk.DidChange(type, version));
            Assert.IsTrue(chunk.DidChange(type, version-1));
        }

        public void AssertHasSharedVersion<T>(Entity e, uint version) where T : struct, ISharedComponentData
        {
            var type = Manager.GetArchetypeChunkSharedComponentType<T>();
            var chunk = Manager.GetChunk(e);
            Assert.AreEqual(version, chunk.GetComponentVersion(type));
            Assert.IsFalse(chunk.DidChange(type, version));
            Assert.IsTrue(chunk.DidChange(type, version-1));
        }

        class EntityForEachSystem : ComponentSystem
        {
            protected override void OnUpdate() {  }
        }
        protected EntityQueryBuilder Entities
        {
            get
            {
                return new EntityQueryBuilderBuilder(World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<EntityForEachSystem>()).Build();
            }
        }

        public unsafe struct EntityQueryBuilderBuilder
        {
            public EntityQueryBuilderBuilder(ComponentSystem system)
            {
                m_System = system;
                m_Any = default;
                m_None = default;
                m_All = default;
                m_AnyWritableBitField = (m_AllWritableBitField = 0u);
                m_Options = EntityQueryOptions.Default;
                m_Query = null;
            }

            public EntityQueryBuilder Build()
            {
                return UnsafeUtilityEx.AsRef<EntityQueryBuilder>(UnsafeUtility.AddressOf(ref this));
            }

            public ComponentSystem m_System;

            public uint m_AnyWritableBitField;

            public uint m_AllWritableBitField;

            public FixedListInt64 m_Any;

            public FixedListInt64 m_None;

            public FixedListInt64 m_All;

            public EntityQueryOptions m_Options;

            public EntityQuery m_Query;
        }

        public EmptySystem EmptySystem
        {
            get
            {
                return World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<EmptySystem>();
            }
        }
    }
}
