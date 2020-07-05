using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Vella.Events;
using Vella.Tests.Attributes;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;
using Vella.Tests.Helpers;
 
public unsafe class EventQueueTests : EscQueueTestsFixture
{
    [Test, TestCategory(TestCategory.Functionality)]
    unsafe public void ZeroSizedComponent()
    {
        var queue = EnqueueComponent<EcsTestDataZeroSized>();

        Assert.AreEqual(queue.ComponentCount(), 1);
    }

    [Test, TestCategory(TestCategory.Functionality)]
    unsafe public void EnqueuesComponent()
    {
        var queue = EnqueueComponent<EcsTestData>();

        Assert.AreEqual(queue.ComponentCount(), 1);
    }

    //[Test, TestCategory(TestCategory.Functionality)]
    //public void EnqueuesComponentsFromArray()
    //{
    //    var (baseQueue, componentQueue, bufferQueue) = new QueueRig<EcsTestData, EcsIntElement>(Allocator.Temp);

    //    componentQueue.Enqueue(new NativeArrayBuilder<EcsTestData>
    //    {
    //        new EcsTestData { value = 2 },
    //        new EcsTestData { value = 3 },
    //        new EcsTestData { value = 4 }
    //    });

    //    Assert.AreEqual(baseQueue.ComponentCount(), 3);
    //}

    [Test, TestCategory(TestCategory.Functionality)]
    public void EnqueuesBufferFromArray()
    {
        var testData = GetDefaultTestData();

        var queue = EnqueueBuffer(testData.Component, testData.Buffer);

        Assert.AreEqual(queue.ComponentCount(), 1);
        Assert.AreEqual(queue.LinksCount(), 1);
        Assert.AreEqual(queue.BufferElementCount(), testData.Buffer.Length);
    }

    [Test, TestCategory(TestCategory.Functionality)]
    public void EnqueuesFromBurst()
    {
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig<EcsTestData, EcsIntElement>(Allocator.Temp);

        new EnqueuesFromBurstJob
        {
            Events = componentQueue,

        }.Schedule().Complete();

        new EnqueuesFromBurstJob
        {
            Events = componentQueue,

        }.Run();

        Assert.AreEqual(2, baseQueue.ComponentCount());
    }

    [BurstCompile]
    public struct EnqueuesFromBurstJob : IJob
    {
        internal EventQueue<EcsTestData> Events;

        public void Execute()
        {
            Events.Enqueue(new EcsTestData
            {
                value = 42
            });
        }
    }

    [Test, TestCategory(TestCategory.Integrity)]
    public void EnqueuesToCorrectThread([Values(1, 10, 100)] int jobCount, [Values(1, 2, 10)] int numPerThread)
    {
        var threadIds = new NativeArray<int>(jobCount, Allocator.TempJob);
        var threadUsages = new NativeArray<int>(JobsUtility.MaxJobThreadCount, Allocator.TempJob);
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig<EcsTestData, EcsIntElement>(Allocator.TempJob);

        try
        {
            var handle = IJobParallelForExtensions.Schedule(new EnqueuesToCorrectThreadJob
            {
                Events = componentQueue,
                ThreadIds = threadIds,
                ThreadUsages = threadUsages

            }, jobCount, numPerThread);

            handle.Complete();

            for (int i = 0; i < jobCount; i++)
            {
                var threadId = threadIds[i];
                var usageCount = threadUsages[threadId];
                if (usageCount == 0)
                    continue;

                var size = baseQueue.GetComponentsForThread(threadId).Length;
                var componentCountForThread = size / sizeof(EcsTestData);

                if (usageCount != componentCountForThread)
                    Debugger.Break();

                Assert.AreEqual(usageCount, componentCountForThread);
            }
        }
        finally
        {
            threadIds.Dispose();
            threadUsages.Dispose();
            baseQueue.Dispose();
        }
    }



    [BurstCompile]
    public struct EnqueuesToCorrectThreadJob : IJobParallelFor
    {
        public EventQueue<EcsTestData> Events;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<int> ThreadIds;

        [NativeDisableContainerSafetyRestriction]
        public NativeArray<int> ThreadUsages;

#pragma warning disable IDE0044, CS0649
        [NativeSetThreadIndex]
        private int _threadIndex;
#pragma warning restore IDE0044, CS0649


        public void Execute(int index)
        {
            Events.Enqueue(new EcsTestData
            {
                value = _threadIndex
            });

            ThreadIds[index] = _threadIndex;
            ThreadUsages[_threadIndex]++;
        }
    }

    //[Test, TestCategory(TestCategory.Integrity)]
    //public void GetDataByThreadId()
    //{
    //    var queue = EnqueueComponent<EcsTestData>();

    //    Assert.Throws<ArgumentException>(() =>
    //    {
    //        queue.GetComponentsForThread(MultiAppendBuffer.MinThreadIndex - 1);
    //    });

    //    Assert.Throws<ArgumentException>(() =>
    //    {
    //        queue.GetLinksForThread(MultiAppendBuffer.MinThreadIndex - 1);
    //    });

    //    Assert.Throws<ArgumentException>(() =>
    //    {
    //        queue.GetBuffersForThread(MultiAppendBuffer.MinThreadIndex - 1);
    //    });

    //    Assert.Throws<ArgumentException>(() =>
    //    {
    //        queue.GetComponentsForThread(MultiAppendBuffer.MaxThreadIndex + 1);
    //    });

    //    Assert.Throws<ArgumentException>(() =>
    //    {
    //        queue.GetLinksForThread(MultiAppendBuffer.MaxThreadIndex + 1);
    //    });

    //    Assert.Throws<ArgumentException>(() =>
    //    {
    //        queue.GetBuffersForThread(MultiAppendBuffer.MaxThreadIndex + 1);
    //    });

    //    Assert.DoesNotThrow(() =>
    //    {
    //        queue.GetComponentsForThread(MultiAppendBuffer.DefaultThreadIndex);
    //        queue.GetLinksForThread(MultiAppendBuffer.DefaultThreadIndex);
    //        queue.GetBuffersForThread(MultiAppendBuffer.DefaultThreadIndex);
    //    });
    //}

    [Test, TestCategory(TestCategory.Functionality)]
    public void EnqueuesFromDynamicBuffer()
    {
        EventQueue baseQueue = EnqueueBufferFromChunkJobDynamicBuffer();

        Assert.AreEqual(baseQueue.ComponentCount(), 2);
        Assert.AreEqual(baseQueue.LinksCount(), 2);
        Assert.AreEqual(baseQueue.BufferElementCount(), 6);
    }

    private EventQueue EnqueueBufferFromChunkJobDynamicBuffer()
    {
        var components = new[]
        {
            ComponentType.ReadWrite<EcsTestData2>(),
            ComponentType.ReadWrite<EcsIntElement>()
        };

        var query = Manager.CreateEntityQuery(components);
        var arch = Manager.CreateArchetype(components);
        var entity = Manager.CreateEntity(arch);

        var testComponent = GetDefaultComponent2();
        Manager.SetComponentData(entity, testComponent);

        var testBuffer = GetDefaultBufferData();
        var buffer = Manager.GetBuffer<EcsIntElement>(entity);
        buffer.AddRange(testBuffer);

        var (baseQueue, componentQueue, bufferQueue) = new QueueRig<EcsTestData, EcsIntElement>(Allocator.Temp);

        var handle1 = new EnqueuesFromDynamicBufferJob
        {
            ComponentType = Manager.GetArchetypeChunkComponentType<EcsTestData2>(true),
            BufferType = Manager.GetArchetypeChunkBufferType<EcsIntElement>(true),
            Events = bufferQueue

        }.ScheduleSingle(query);

        var handle2 = new EnqueuesFromDynamicBufferJob
        {
            ComponentType = Manager.GetArchetypeChunkComponentType<EcsTestData2>(true),
            BufferType = Manager.GetArchetypeChunkBufferType<EcsIntElement>(true),
            Events = bufferQueue

        }.ScheduleSingle(query);


        EmptySystem.AddDependencies(handle1, handle2);
        handle1.Complete();
        handle2.Complete();

        return baseQueue;
    }

    public struct EnqueuesFromDynamicBufferJob : IJobChunk
    {
        [ReadOnly] public ArchetypeChunkComponentType<EcsTestData2> ComponentType;
        [ReadOnly] public ArchetypeChunkBufferType<EcsIntElement> BufferType;
        public EventQueue<EcsTestData, EcsIntElement> Events;

#pragma warning disable IDE0044, CS0649
        [NativeSetThreadIndex]
        private int _threadIndex;
#pragma warning restore IDE0044, CS0649

        public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
        {
            var components = chunk.GetNativeArray(ComponentType);
            var buffers = chunk.GetBufferAccessor(BufferType);

            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                var buffer = buffers[i];

                Events.Enqueue(new EcsTestData
                {
                    value = component.value0,

                }, buffer);
            }
        }
    }

    [Test, TestCategory(TestCategory.Functionality)]
    public void ClearsDataProperly()
    {
        var queue = EnqueueComponent<EcsTestData>();

        queue.Clear();

        Assert.AreEqual(0, queue.Count());
        Assert.AreEqual(0, queue.ComponentCount());
        Assert.AreEqual(0, queue.LinksCount());
        Assert.AreEqual(0, queue.BufferElementCount());
    }

    [Test, TestCategory(TestCategory.Compatibility)]
    public void ZeroSizedBufferElement()
    {
        var bufferElements = new NativeArray<EcsTestElementZeroSized>(10, Allocator.Temp);

        // Im not sure there's ever a case where having a zero sized buffer element would be useful?

        var queue = EnqueueBuffer<EcsTestDataZeroSized, EcsTestElementZeroSized>(default, bufferElements);

        Assert.AreEqual(queue.Count(), 1);
        Assert.AreEqual(queue.ComponentCount(), 1);
        Assert.AreEqual(queue.LinksCount(), 1);
        Assert.AreEqual(queue.BufferElementCount(), bufferElements.Length);
    }

    [Test, TestCategory(TestCategory.Functionality)]
    public void ReadsMetaComponents([Values(0, 10)] int componentCount)
    {
        var baseQueue = new EventQueue(TypeManager.GetTypeIndex<EcsTestData>(), UnsafeUtility.SizeOf<EcsTestData>(), TypeManager.GetTypeIndex<EcsIntElement>(), UnsafeUtility.SizeOf<EcsIntElement>(), Allocator.Temp);
        var componentQueue = baseQueue.Cast<EventQueue<EcsTestData>>();
        var source = new NativeArray<EntityEvent>(componentCount, Allocator.Temp);
        var destination = new NativeArray<EntityEvent>(componentCount, Allocator.Temp);
        var set = new HashSet<int>();

        for (int i = 0; i < source.Length; i++)
        {
            const int threadIndex = MultiAppendBuffer.DefaultThreadIndex;
            ref var metaBuffer = ref baseQueue._metaData.GetBuffer(threadIndex);
            var id = EventQueue.CreateIdHash((int)baseQueue._metaData.Ptr, threadIndex, i * sizeof(EntityEvent));
            source[i] = new EntityEvent
            {
                Id = id,
                ComponentTypeIndex = baseQueue._componentTypeIndex,
                BufferTypeIndex = baseQueue._bufferTypeIndex
            };
            set.Add(id);
        }

        for (int i = 0; i < source.Length; i++)
            componentQueue.Enqueue(new EcsTestData { value = i });

        Assert.DoesNotThrow(() =>
        {
            var reader = baseQueue.GetMetaReader();
            reader.CopyTo(destination.GetUnsafePtr(), source.Length * sizeof(EntityEvent));
        });

        AssertBytesAreEqual(source, destination);
    }

    [Test, TestCategory(TestCategory.Functionality)]
    public void UniqueEventIds([Values(100000)] int componentCount)
    {
        var baseQueue = new EventQueue(TypeManager.GetTypeIndex<EcsTestData>(), UnsafeUtility.SizeOf<EcsTestData>(), default, default, Allocator.Temp);
        var componentQueue = baseQueue.Cast<EventQueue<EcsTestData>>();
        var set = new HashSet<int>();

        var queues = new List<EventQueue>();
        {
            new EventQueue(0, 0, 0, 0, Allocator.Temp);
            new EventQueue(1, 0, 0, 0, Allocator.Temp);
            new EventQueue(4, 0, 0, 0, Allocator.Temp);
            new EventQueue(8, 0, 0, 0, Allocator.Temp);
            new EventQueue(0, 0, 1, 0, Allocator.Temp);
            new EventQueue(0, 0, 4, 0, Allocator.Temp);
            new EventQueue(0, 0, 8, 0, Allocator.Temp);
            new EventQueue(12, 0, 0, 0, Allocator.Temp);
            new EventQueue(236, 0, 0, 0, Allocator.Temp);
            new EventQueue(512, 0, 0, 0, Allocator.Temp);
            new EventQueue(4, 0, 4, 0, Allocator.Temp);
            new EventQueue(16, 0, 16, 0, Allocator.Temp);
            new EventQueue(236, 0, 32, 0, Allocator.Temp);
            new EventQueue(1024, 0, 1024, 0, Allocator.Temp);
            new EventQueue(2048, 0, 2048, 0, Allocator.Temp);
        };

        Assert.DoesNotThrow(() =>
        {
            foreach(var q in queues)
            {
                for (int i = 0; i < componentCount; i++)
                {
                    for (int j = MultiAppendBuffer.MinThreadIndex; j <= MultiAppendBuffer.MinThreadIndex; j++)
                    {
                        ref var metaBuffer = ref q._metaData.GetBuffer(j);
                        var id = EventQueue.CreateIdHash((int)q._metaData.Ptr, (int)metaBuffer.Ptr, j * sizeof(EntityEvent));
                        set.Add(id);
                    }
                }
            }
        });

    }

    [Test, TestCategory(TestCategory.Functionality)]
    public void ReadsQueuedComponents([Values(0, 10)] int componentCount)
    {
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig<EcsTestData, EcsIntElement>(Allocator.Temp);
        var source = new NativeArray<EcsTestData>(componentCount, Allocator.Temp);
        var destination = new NativeArray<EcsTestData>(componentCount, Allocator.Temp);

        for (int i = 0; i < source.Length; i++)
            source[i] = new EcsTestData { value = i };

        for (int i = 0; i < source.Length; i++)
            componentQueue.Enqueue(new EcsTestData { value = i });


        Assert.DoesNotThrow(() =>
        {
            var reader = baseQueue.GetComponentReader();
            reader.CopyTo(destination.GetUnsafePtr(), source.Length * sizeof(EcsTestData));
        });

        AssertBytesAreEqual(source, destination);
    }

    [Test, TestCategory(TestCategory.Functionality)]
    public void ReadsQueuedBuffers([Values(0, 10, 100)] int componentCount, [Values(0, 1, 10)] int bufferElementCount)
    {
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig<EcsTestData, EcsIntElement>(Allocator.Temp);

        var components = new NativeArray<EcsTestData>(componentCount, Allocator.Temp);
        var links = new NativeArray<BufferLink>(componentCount, Allocator.Temp);

        for (int i = 0; i < componentCount; i++)
        {
            var elements = stackalloc EcsIntElement[bufferElementCount];
            for (int j = 0; j < bufferElementCount; j++)
                elements[j] = j;

            bufferQueue.Enqueue(new EcsTestData { value = i }, elements, bufferElementCount);
        }

        Assert.DoesNotThrow(() =>
        {
            baseQueue.GetComponentReader().CopyTo(components.GetUnsafePtr(), componentCount * sizeof(EcsTestData));
            baseQueue.GetLinksReader().CopyTo(links.GetUnsafePtr(), componentCount * sizeof(BufferLink));

            for (int i = 0; i < componentCount; i++)
            {
                var component = components[i];
                Assert.AreEqual(component.value, i);

                BufferLink link = links[i];

                var appendBuffer = baseQueue._bufferData.GetBuffer(link.ThreadIndex);
                Assert.IsTrue(appendBuffer.Ptr != null);
                Assert.LessOrEqual(link.Offset, appendBuffer.Length);

                EcsIntElement* element = (EcsIntElement*)(appendBuffer.Ptr + link.Offset);
                Assert.IsTrue(element != null);

                for (int j = 0; j < link.Length; j++)
                    Assert.AreEqual(element[j].Value, j); 
            }
        });
    }
}

