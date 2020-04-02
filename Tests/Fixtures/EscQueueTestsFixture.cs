using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Vella.Events;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;
using Vella.Tests.Helpers;

namespace Vella.Tests.Fixtures
{
    public class EscQueueTestsFixture : ECSTestsFixture
    {
        protected struct QueueRig<T1, T2>
            where T1 : struct, IComponentData
            where T2 : unmanaged, IBufferElementData
        {
            private Allocator _allocator;

            public QueueRig(Allocator allocator)
            {
                _allocator = allocator;
            }

            public void Deconstruct(out EventQueue baseQueue, out EventQueue<T1> componentQueue, out EventQueue<T1, T2> bufferQueue)
            {
                baseQueue = new EventQueue(TypeManager.GetTypeIndex<T1>(), UnsafeUtility.SizeOf<T1>(), TypeManager.GetTypeIndex<T2>(), UnsafeUtility.SizeOf<T2>(), _allocator);
                componentQueue = baseQueue.Cast<EventQueue<T1>>();
                bufferQueue = baseQueue.Cast<EventQueue<T1, T2>>();
            }
        }

        protected static (EcsTestData Component, NativeArray<EcsIntElement> Buffer) GetDefaultTestData()
        {
            return (GetDefaultComponent(), GetDefaultBufferData());
        }

        protected static (EcsTestData2 Component, NativeArray<EcsIntElement2> Buffer) GetDefaultTestData2()
        {
            return (GetDefaultComponent2(), GetDefaultBufferData2());
        }

        protected static EcsTestData GetDefaultComponent()
        {
            return new EcsTestData
            {
                value = 1,
            };
        }

        protected static EcsTestData2 GetDefaultComponent2()
        {
            return new EcsTestData2
            {
                value0 = 1,
                value1 = 2
            };
        }

        protected static NativeArray<EcsIntElement> GetDefaultBufferData()
        {
            return new NativeArrayBuilder<EcsIntElement>
            {
                new EcsIntElement
                {
                    Value = 1,
                },
                new EcsIntElement
                {
                    Value = 3,
                },
                new EcsIntElement
                {
                    Value = 5,
                },
            };
        }

        protected static NativeArray<EcsIntElement2> GetDefaultBufferData2()
        {
            return new NativeArrayBuilder<EcsIntElement2>
            {
                new EcsIntElement2
                {
                    Value0 = 1,
                    Value1 = 2
                },
                new EcsIntElement2
                {
                    Value0 = 2,
                    Value1 = 3
                },
                new EcsIntElement2
                {
                    Value0 = 4,
                    Value1 = 5
                },
            };
        }

        protected static EventQueue EnqueueComponent<T>(T component = default) where T : struct, IComponentData
        {
            var (baseQueue, componentQueue, bufferQueue) = new QueueRig<T, EcsIntElement>(Allocator.Temp);

            componentQueue.Enqueue(component);

            return baseQueue;
        }

        protected static EventQueue EnqueueBuffer<T1, T2>(T1 component, NativeArray<T2> bufferElements)
            where T1 : struct, IComponentData
            where T2 : unmanaged, IBufferElementData
        {
            var (baseQueue, componentQueue, bufferQueue) = new QueueRig<T1, T2>(Allocator.Temp);

            bufferQueue.Enqueue(component, bufferElements);

            return baseQueue;
        }

        public int GetMainThreadId() => EmptySystem.GetMainThreadIndex();
    }

}