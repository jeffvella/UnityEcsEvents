using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using System.Runtime.CompilerServices;
using System;
using UnityEngine;
using Unity.Jobs.LowLevel.Unsafe;
using System.Diagnostics;

namespace Vella.Events
{
    /// <summary>
    /// A collection that allows systems/jobs to schedule event components to be created later in the frame.
    /// <para>
    /// Event components are attached to new entities by the <see cref="EntityEventSystem'"/>
    /// and exist for one frame only (until <see cref="EntityEventSystem"/>.OnUpdate() runs again).
    /// </para>
    /// <para>
    /// This is intended to be passed into jobs, where it will be injected with thread index 
    /// and utilize the corresponding queue dedicated for that thread.
    /// </para>Helpers
    /// 
    /// EventQueues do not need to be disposed manually by the systems creating them. 
    /// They are owned and disposed automatically by <see cref="EntityEventSystem"/> 
    /// </summary>
    /// <typeparam name="T">type of event</typeparam>
    public unsafe struct EventQueue<T> where T : struct, IComponentData
    {
#pragma warning disable IDE0044, CS0649
        [NativeSetThreadIndex]
        internal int _threadIndex;
#pragma warning restore IDE0044, CS0649

        internal int _componentSize;
        internal int _bufferElementSize;
        internal int _componentTypeIndex;
        internal int _bufferTypeIndex;

        internal MultiAppendBuffer _metaData;
        internal MultiAppendBuffer _componentData;
        internal MultiAppendBuffer _bufferLinks;
        internal MultiAppendBuffer _bufferData;

        public int Enqueue(T item)
        {
            _componentData.GetBuffer(_threadIndex).Add(item);
            EventQueue.SetEventMeta(_metaData, _componentTypeIndex, _bufferTypeIndex, _threadIndex, out var id);
            return id;
        }
    }

    public unsafe struct EventBufferQueue<T> where T : struct, IBufferElementData
    {
#pragma warning disable IDE0044, CS0649
        [NativeSetThreadIndex]
        internal int _threadIndex;
#pragma warning restore IDE0044, CS0649

        internal int _componentSize;
        internal int _bufferElementSize;
        internal int _componentTypeIndex;
        internal int _bufferTypeIndex;

        internal MultiAppendBuffer _metaData;
        internal MultiAppendBuffer _componentData;
        internal MultiAppendBuffer _bufferLinks;
        internal MultiAppendBuffer _bufferData;

        public int Enqueue(void* items, int length)
        {
            var buffer = _bufferData.GetBuffer(_threadIndex);
            var offset = buffer.Length;

            _bufferLinks.GetBuffer(_threadIndex).Add(new BufferLink
            {
                ThreadIndex = _threadIndex,
                Offset = offset,
                Length = length,
            });

            _bufferData.GetBuffer(_threadIndex).Add(items, UnsafeUtility.SizeOf<T>() * length);
            EventQueue.SetEventMeta(_metaData, _componentTypeIndex, _bufferTypeIndex, _threadIndex, out var id);
            return id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Enqueue(NativeArray<T> array)
            => Enqueue(array.GetUnsafePtr(), array.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Enqueue(DynamicBuffer<T> buffer)
        {
            var header = *(BufferHeaderProxy**)UnsafeUtility.AddressOf(ref buffer);
            var ptr = BufferHeaderProxy.GetElementPointer(header);
            return Enqueue(ptr, buffer.Length);
        }
    }

    /// <summary>
    /// A collection that allows systems/jobs to schedule event components to be created later in the frame.
    /// <para>
    /// Event components are attached to new entities by the <see cref="EntityEventSystem'"/>
    /// and exist for one frame only (until <see cref="EntityEventSystem"/>.OnUpdate() runs again).
    /// </para>
    /// <para>
    /// This is intended to be passed into jobs, where it will be injected with thread index 
    /// and utilize the corresponding queue dedicated for that thread.
    /// </para>
    /// EventQueues do not need to be disposed manually by the systems creating them. 
    /// They are owned and disposed automatically by <see cref="EntityEventSystem"/>
    /// </summary>
    /// <typeparam name="TComponent">type of event component</typeparam>
    public unsafe struct EventQueue<TComponent,TBufferData> 
        where TComponent : struct, IComponentData
        where TBufferData : unmanaged, IBufferElementData
    {
#pragma warning disable IDE0044, CS0649
        [NativeSetThreadIndex]
        internal int _threadIndex;
#pragma warning restore IDE0044, CS0649

        internal int _componentSize;
        internal int _bufferElementSize;
        internal int _componentTypeIndex;
        internal int _bufferTypeIndex;

        internal MultiAppendBuffer _metaData;
        internal MultiAppendBuffer _componentData;
        internal MultiAppendBuffer _bufferLinks;
        internal MultiAppendBuffer _bufferData;

        public int Enqueue(TComponent item, void* items, int length)
        {
            _componentData.GetBuffer(_threadIndex).Add(item);

            var buffer = _bufferData.GetBuffer(_threadIndex);
            var offset = buffer.Length;

            _bufferLinks.GetBuffer(_threadIndex).Add(new BufferLink
            {
                ThreadIndex = _threadIndex,
                Offset = offset,
                Length = length,
            });

            _bufferData.GetBuffer(_threadIndex).Add(items, UnsafeUtility.SizeOf<TBufferData>() * length);
            EventQueue.SetEventMeta(_metaData, _componentTypeIndex, _bufferTypeIndex, _threadIndex, out var id);
            return id;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Enqueue(TComponent component, NativeArray<TBufferData> array)
            => Enqueue(component, array.GetUnsafePtr(), array.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Enqueue(TComponent item, DynamicBuffer<TBufferData> buffer)
        {
            var header = *(BufferHeaderProxy**)UnsafeUtility.AddressOf(ref buffer);
            var ptr = BufferHeaderProxy.GetElementPointer(header);
            return Enqueue(item, ptr, buffer.Length);
        }
    }

    /// <summary>
    /// Wraps management of a queue for storing up components from multiple sources (c# events/systems/jobs/threads).
    /// This untyped version is a union with <see cref="EventQueue{T}"/> and <see cref="EventQueue{TComponent, TBufferData}"/>
    /// </summary>
    public unsafe struct EventQueue
    {
#pragma warning disable IDE0044, CS0649
        [NativeSetThreadIndex]
        internal int _threadIndex;
#pragma warning restore IDE0044, CS0649

        internal int _componentSize;
        internal int _bufferElementSize;
        internal int _componentTypeIndex;
        internal int _bufferTypeIndex;

        internal MultiAppendBuffer _metaData;
        internal MultiAppendBuffer _componentData;
        internal MultiAppendBuffer _bufferLinks;
        internal MultiAppendBuffer _bufferData;

        public EventQueue(int componentTypeIndex, int componentSize, int bufferTypeIndex, int bufferElementSize, Allocator allocator) : this()
        {
            _metaData = new MultiAppendBuffer(allocator, sizeof(EntityEvent));

            if (componentSize > 0)
            {
                _componentData = new MultiAppendBuffer(allocator);
            }
            if (bufferElementSize > 0)
            {
                _bufferLinks = new MultiAppendBuffer(allocator);
                _bufferData = new MultiAppendBuffer(allocator);
            }
            _threadIndex = MultiAppendBuffer.DefaultThreadIndex;
            _componentSize = componentSize;
            _bufferElementSize = bufferElementSize;
            _componentTypeIndex = componentTypeIndex;
            _bufferTypeIndex = bufferTypeIndex;
        }

        internal static void SetEventMeta(MultiAppendBuffer metaData, int componentTypeIndex, int BufferTypeIndex, int threadIndex, out int id)
        {
            ref var metaBuffer = ref metaData.GetBuffer(threadIndex);
            id = CreateIdHash((int)metaData.Ptr, threadIndex, metaBuffer.Length);
            metaBuffer.Add(new EntityEvent { 
                Id = id,
                ComponentTypeIndex = componentTypeIndex,
                BufferTypeIndex = BufferTypeIndex
            });
        }

        internal static int CreateIdHash(int a, int b, int c)
        {
            int hash = 17;
            hash = hash * 23 + a;
            hash = hash * 23 + b;
            hash = hash * 23 + c;
            return hash;
        }

        public static EventQueue<T> CreateWithComponent<T>(Allocator allocator) where T : struct, IComponentData
            => new EventQueue(TypeManager.GetTypeIndex<T>(), UnsafeUtility.SizeOf<T>(), default, default, allocator).Cast<EventQueue<T>>();

        public static EventBufferQueue<T> CreateWithBuffer<T>(Allocator allocator) where T : unmanaged, IBufferElementData
            => new EventQueue(default, default, TypeManager.GetTypeIndex<T>(), UnsafeUtility.SizeOf<T>(), allocator).Cast<EventBufferQueue<T>>();

        public static EventQueue<TComponent, TBufferData> CreateWithComponentAndBuffer<TComponent, TBufferData>(Allocator allocator)
            where TComponent : struct, IComponentData
            where TBufferData : unmanaged, IBufferElementData
        {
            return new EventQueue(UnsafeUtility.SizeOf<TComponent>(), TypeManager.GetTypeIndex<TComponent>(), TypeManager.GetTypeIndex<TBufferData>(), UnsafeUtility.SizeOf<TBufferData>(), allocator)
                .Cast<EventQueue<TComponent, TBufferData>>();
        }

        public int Count() => _metaData.Size() / sizeof(EntityEvent);

        public int ComponentCount() => _componentData.Size() / _componentSize;

        public int LinksCount() => _bufferLinks.Size() / UnsafeUtility.SizeOf<BufferLink>();

        public int BufferElementCount() => _bufferData.Size() / _bufferElementSize;

        public MultiAppendBuffer.Reader GetMetaReader() => _metaData.AsReader();

        public MultiAppendBuffer.Reader GetComponentReader() => _componentData.AsReader();

        public MultiAppendBuffer.Reader GetLinksReader() => _bufferLinks.AsReader();

        public MultiAppendBuffer.Reader GetBuffersReader() => _bufferData.AsReader();

        public ref UnsafeAppendBuffer GetMetaForThread(int threadIndex = MultiAppendBuffer.DefaultThreadIndex)
        {
            return ref _metaData.GetBuffer(threadIndex);
        }

        public ref UnsafeAppendBuffer GetComponentsForThread(int threadIndex = MultiAppendBuffer.DefaultThreadIndex)
        {
            return ref _componentData.GetBuffer(threadIndex);
        }

        public ref UnsafeAppendBuffer GetLinksForThread(int threadIndex = MultiAppendBuffer.DefaultThreadIndex)
        {
            return ref _bufferLinks.GetBuffer(threadIndex);
        }

        public ref UnsafeAppendBuffer GetBuffersForThread(int threadIndex = MultiAppendBuffer.DefaultThreadIndex)
        {
            return ref _bufferData.GetBuffer(threadIndex);
        }

        public T Cast<T>() where T : struct => UnsafeUtilityEx.AsRef<T>(UnsafeUtility.AddressOf(ref this));

        public void Clear()
        {
            _metaData.Clear();
            if (_componentSize != 0)
            {
                _componentData.Clear();
            }
            if (_bufferElementSize != 0)
            {
                _bufferLinks.Clear();
                _bufferData.Clear();
            }
        }

        public void Dispose()
        {
            _metaData.Dispose();
            if (_componentSize != 0)
            {
                _componentData.Dispose();
            }
            if (_bufferElementSize != 0)
            {
                _bufferLinks.Dispose();
                _bufferData.Dispose();
            }
        }

        public void EnqueueDefault()
        {
            var item = stackalloc byte[_componentSize];
            _componentData.GetBuffer(_threadIndex).Add(item, _componentSize);
        }

        public void EnqueueComponent(byte* ptr)
        {
            _componentData.GetBuffer(_threadIndex).Add(ptr, _componentSize);
        }

        public void Enqueue<T>(T item) where T : unmanaged
        {
            _componentData.GetBuffer(_threadIndex).Add(&item, sizeof(T));
        }

        public void Enqueue<T>(byte* ptr) where T : unmanaged
        {
            _componentData.GetBuffer(_threadIndex).Add(ptr, sizeof(T));
        }

        public void Enqueue(byte* ptr, int length)
        {
            _componentData.GetBuffer(_threadIndex).Add(ptr, length);
        }
    }

}
