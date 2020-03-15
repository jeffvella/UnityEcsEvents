using NUnit.Framework;
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Vella.Events;
using Vella.Tests.Data;
using Vella.Tests.Fixtures;

public unsafe partial class EventQueueTests : ECSTestsFixture
{
    public struct QueueRig
    {
        public void Deconstruct(out EventQueue baseQueue, out EventQueue<EcsTestData> componentQueue, out EventQueue<EcsTestData, EcsIntElement> bufferQueue) 
        {
            baseQueue = new EventQueue(UnsafeUtility.SizeOf<EcsTestData>(), UnsafeUtility.SizeOf<EcsIntElement>(), Allocator.Temp);
            componentQueue = baseQueue.Cast<EventQueue<EcsTestData>>();
            bufferQueue = baseQueue.Cast<EventQueue<EcsTestData, EcsIntElement>>();
        }
    }

    [Test, Category("Functionality")]
    unsafe public void EnqueuesComponent()
    {
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig();

        componentQueue.Enqueue(new EcsTestData
        {
            value = 6
        });

        Assert.AreEqual(baseQueue.ComponentCount(), 1);
    }

    [Test, Category("Functionality")]
    public void EnqueuesComponentsFromArray()
    {
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig();

        componentQueue.Enqueue(new NativeArrayBuilder<EcsTestData>
        {
            new EcsTestData { value = 2 },
            new EcsTestData { value = 3 },
            new EcsTestData { value = 4 }
        });

        Assert.AreEqual(baseQueue.ComponentCount(), 3);
    }

    [Test, Category("Functionality")]
    public void EnqueuesBufferFromArray()
    {
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig();

        var component = new EcsTestData { value = 1 };
        var bufferElements = new NativeArrayBuilder<EcsIntElement>
        {
            new EcsIntElement { Value = 2 },
            new EcsIntElement { Value = 3 },
            new EcsIntElement { Value = 4 }
        };

        bufferQueue.Enqueue(component, bufferElements);

        Assert.AreEqual(baseQueue.ComponentCount(), 1);
        Assert.AreEqual(baseQueue.BufferElementCount(), 3);
    }

    [Test]
    public void EnqueuesBufferFromAccessor()
    {

    }

    [Test]
    public void EnqueuesFromJob()
    {

    }

    [Test]
    public void EnqueuesToCorrectThread()
    {

    }

    [Test]
    public void ClearsDataProperly()
    {

    }

    [Test]
    public void ZeroSizedComponent()
    {

    }

    [Test]
    public void ZeroSizedBufferElement()
    {

    }

    [Test]
    public void CachedCountUpdates()
    {

    }

    [Test]
    public void ReadsQueuedComponents([Values(0, 10)] int componentCount)
    {
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig();
        var source = new NativeArray<EcsTestData>(componentCount, Allocator.Temp);
        var destination = new NativeArray<EcsTestData>(componentCount, Allocator.Temp);

        for (int i = 0; i < source.Length; i++)
            source[i] = new EcsTestData { value = i };

        componentQueue.Enqueue(source);

        Assert.DoesNotThrow(() =>
        {
            var reader = baseQueue.GetComponentReader();
            reader.CopyTo(destination.GetUnsafePtr(), source.Length * sizeof(EcsTestData));
        });

        AssertBytesAreEqual(source, destination);
    }

    [Test]
    public void ReadsQueuedBuffers([Values(0, 10, 100)] int componentCount, [Values(0, 1, 10)] int bufferElementCount)
    {
        var (baseQueue, componentQueue, bufferQueue) = new QueueRig();

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
                Assert.LessOrEqual(link.Offset, appendBuffer.Size);

                EcsIntElement* element = (EcsIntElement*)(appendBuffer.Ptr + link.Offset);
                Assert.IsTrue(element != null);

                for (int j = 0; j < link.Length; j++)
                    Assert.AreEqual(element[j].Value, j);
            }
        });
    }
}

