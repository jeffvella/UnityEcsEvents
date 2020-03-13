using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using System.Runtime.CompilerServices;

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
    /// and utilize the corresponding queue dedicated to that thread.
    /// </para>
    /// EventQueues do not need to be disposed manually by the systems creating them. 
    /// They are owned and disposed automatically by <see cref="EntityEventSystem"/>
    /// </summary>
    /// <typeparam name="T">type of event</typeparam>
    public unsafe struct EventQueue<T> where T : struct
    {
        [NativeSetThreadIndex]
        private int _threadIndex;

        private UnsafeMultiAppendBuffer _data;
        private UnsafeMultiAppendBuffer _bufferMap;
        private UnsafeMultiAppendBuffer _bufferData;

        public void Enqueue(T item) => _data.GetBuffer(_threadIndex).Add(item);

        public void Enqueue<T2>(T item, T2* items, int length) where T2 : unmanaged
        {
            _data.GetBuffer(_threadIndex).Add(item);

            var buffer = _bufferData.GetBuffer(_threadIndex);
            var offset = buffer.Size;

            _bufferMap.GetBuffer(_threadIndex).Add(new BufferLink
            {
                ThreadIndex = _threadIndex,
                Offset = offset,
                Length = length,
            });

            _bufferData.GetBuffer(_threadIndex).AddArray<T2>(items, length);
        }

        public void Enqueue(NativeArray<T> items, int length)
            => _data.GetBuffer(_threadIndex).AddArray<T>(items.GetUnsafePtr(), length);
    }

    /// <summary>
    /// A collection that allows systems/jobs to schedule event components to be created later in the frame.
    /// <para>
    /// Event components are attached to new entities by the <see cref="EntityEventSystem'"/>
    /// and exist for one frame only (until <see cref="EntityEventSystem"/>.OnUpdate() runs again).
    /// </para>
    /// <para>
    /// This is intended to be passed into jobs, where it will be injected with thread index 
    /// and utilize the corresponding queue dedicated to that thread.
    /// </para>
    /// EventQueues do not need to be disposed manually by the systems creating them. 
    /// They are owned and disposed automatically by <see cref="EntityEventSystem"/>
    /// </summary>
    /// <typeparam name="TComponent">type of event component</typeparam>
    public unsafe struct EventQueue<TComponent,TBufferData> 
        where TComponent : struct, IComponentData
        where TBufferData : unmanaged, IBufferElementData
    {
        [NativeSetThreadIndex]
        private int _threadIndex;

        private UnsafeMultiAppendBuffer _data;
        private UnsafeMultiAppendBuffer _bufferMap;
        private UnsafeMultiAppendBuffer _bufferData;

        public void Enqueue(TComponent item, void* items, int length)
        {
            _data.GetBuffer(_threadIndex).Add(item);

            var buffer = _bufferData.GetBuffer(_threadIndex);
            var offset = buffer.Size;

            _bufferMap.GetBuffer(_threadIndex).Add(new BufferLink
            {
                ThreadIndex = _threadIndex,
                Offset = offset,
                Length = length,
            });

            _bufferData.GetBuffer(_threadIndex).AddArray<TBufferData>(items, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(TComponent component, NativeArray<TBufferData> array)
            => Enqueue(component, array.GetUnsafePtr(), array.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue(TComponent item, BufferAccessor<TBufferData> buffer)
            => Enqueue(item, *(void**)UnsafeUtility.AddressOf(ref buffer), buffer.Length);
    }

    /// <summary>
    /// Wraps management of a queue for storing up components from multiple sources (c# events/systems/jobs/threads).
    /// This untyped version is a union with <see cref="EventQueue{T}"/>
    /// </summary>
    internal unsafe struct EventQueue
    {
        [NativeSetThreadIndex]
        private int _threadIndex;

        private UnsafeMultiAppendBuffer _componentData;
        public UnsafeMultiAppendBuffer _bufferLinks;
        public UnsafeMultiAppendBuffer _bufferData;

        private int _componentSize;
        private int _cachedCount;

        public bool HasBuffer;

        public EventQueue(int componentSize, Allocator allocator) : this()
        {
            _componentData = new UnsafeMultiAppendBuffer(allocator);
            _bufferLinks = new UnsafeMultiAppendBuffer(allocator);
            _bufferData = new UnsafeMultiAppendBuffer(allocator);
            _threadIndex = UnsafeMultiAppendBuffer.DefaultThreadIndex;
            _componentSize = componentSize;
        }

        public int CachedCount => _cachedCount;

        public int ComponentCount() => _cachedCount = _componentData.Size() / _componentSize;

        public UnsafeMultiAppendBuffer.Reader GetComponentReader() => _componentData.AsReader();

        public UnsafeMultiAppendBuffer.Reader GetLinksReader() => _bufferLinks.AsReader();

        public UnsafeMultiAppendBuffer.Reader GetBuffersReader() => _bufferLinks.AsReader();

        public T Cast<T>() where T : struct => UnsafeUtilityEx.AsRef<T>(UnsafeUtility.AddressOf(ref this));

        public void Clear()
        {
            _componentData.Clear();
            _bufferLinks.Clear();
            _bufferData.Clear();
        }

        public void Dispose()
        {
            _componentData.Dispose();
            _bufferLinks.Dispose();
            _bufferData.Dispose();
        }


    }

}