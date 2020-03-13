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
    /// and then leverage the corrisponding queue dedicated to that thread.
    /// </para>
    /// EventQueues do not need to be disposed manually by the systems creating them. They are owned and disposed automatically by <see cref="EntityEventSystem"/>
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

        //public void Enqueue(NativeArray<T> items, int length) 
        //    => _data.GetBuffer(_threadIndex).AddArray<T>(items.GetUnsafePtr(), length);
    }

    public unsafe struct EventQueue<T1,T2> 
        where T1 : struct, IComponentData
        where T2 : unmanaged, IBufferElementData
    {
        [NativeSetThreadIndex]
        private int _threadIndex;

        private UnsafeMultiAppendBuffer _data;
        private UnsafeMultiAppendBuffer _bufferMap;
        private UnsafeMultiAppendBuffer _bufferData;

        public void Enqueue(T1 item, void* items, int length)
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

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public void Enqueue(T1 component, NativeArray<T2> array) 
        //    => Enqueue(component, array.GetUnsafePtr(), array.Length);

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        //public void Enqueue(T1 item, BufferAccessor<T2> buffer)
        //{
        //    Enqueue(item, *(void**)UnsafeUtility.AddressOf(ref buffer), buffer.Length);
        //}
    }

    //public unsafe struct EventBufferQueue<T2> where T2 : unmanaged, IBufferElementData
    //{
    //    [NativeSetThreadIndex]
    //    private int _threadIndex;

    //    private UnsafeMultiAppendBuffer _data;
    //    private UnsafeMultiAppendBuffer _bufferMap;
    //    private UnsafeMultiAppendBuffer _bufferData;

    //    public void Enqueue(T2* items, int length)
    //    {
    //        var buffer = _bufferData.GetBuffer(_threadIndex);
    //        var offset = buffer.Size;

    //        _bufferMap.GetBuffer(_threadIndex).Add(new BufferLink
    //        {
    //            ThreadIndex = _threadIndex,
    //            Offset = offset,
    //            Length = length,
    //        });

    //        buffer.AddArray<T2>(items, length);

    //        //.AddArray<TBufferData>((byte*)buffer + sizeof(EventQueueBufferHeader), length);
    //        //var ptr = (byte*)UnsafeUtility.Malloc(size, sizeof(T), Allocator.TempJob);
    //        //var buffer = new BufferHeaderProxy()
    //        //{
    //        //     Pointer = ptr,
    //        //     Capacity = 0,
    //        //     Length = length,
    //        //};
    //        //UnsafeUtility.MemCpy(items, ptr, size);
    //        //_data.GetBuffer(_threadIndex).Add(buffer);
    //    }
    //}



    //public unsafe struct EventQueue<TComponentData, TBufferData>
    //    where TComponentData : struct, IComponentData
    //    where TBufferData : struct, IBufferElementData
    //{
    //    [NativeSetThreadIndex]
    //    private int _threadIndex;

    //    private UnsafeMultiAppendBuffer _componentData;
    //    private UnsafeMultiAppendBuffer _bufferData;
    //    //private UnsafeMultiAppendBuffer _bufferLengths;

    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    public void Enqueue(TComponentData item, BufferAccessor<TBufferData> buffer)
    //    {
    //        Enqueue(item, *(void**)&buffer, buffer.Length);
    //    }

    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    public void Enqueue(TComponentData item, NativeArray<TBufferData> buffer)
    //    {
    //        Enqueue(item, buffer.GetUnsafeReadOnlyPtr(), buffer.Length);
    //    }

    //    public void Enqueue(TComponentData item, void* buffer, int length)
    //    {
    //        _componentData.GetBuffer(_threadIndex).Add(item);
    //        var header = new EventQueueBufferHeader
    //        {
    //            Length = length
    //        };
    //        _bufferData.GetBuffer(_threadIndex).Add(&header, sizeof(EventQueueBufferHeader));
    //        _bufferData.GetBuffer(_threadIndex).AddArray<TBufferData>((byte*)buffer + sizeof(EventQueueBufferHeader), length);
    //        //_bufferLengths.GetBuffer(_threadIndex).Add(length);
    //    }
    //}

    public struct EventQueueBufferHeader
    {
        public int Length;
    }

    public struct BufferLink : IComponentData
    {
        public int ThreadIndex;
        public int Offset;
        public int Length;
    }

    /// <summary>
    /// Wraps management of a queue for storing up components
    /// from multiple sources (c# events/systems/jobs/threads).
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
        //private int _bufferElementSize;
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

        //public EventQueue(int componentSize, int bufferElementSize, Allocator allocator)
        //{
        //    HasBuffer = true;
        //    _componentData = new UnsafeMultiAppendBuffer(allocator);
        //    _bufferData = new UnsafeMultiAppendBuffer(allocator);
        //    //_bufferLengths = new UnsafeMultiAppendBuffer(allocator);
        //    _threadIndex = UnsafeMultiAppendBuffer.DefaultThreadIndex;
        //    _componentSize = componentSize;
        //    _bufferElementSize = bufferElementSize;
        //    _cachedCount = 0;
        //}

        public int CachedCount => _cachedCount;

        public int ComponentCount() => _cachedCount = _componentData.Size() / _componentSize;

        public void CopyComponentsTo(byte* ptr, int sizeBytes) => _componentData.Copy(ptr, sizeBytes);

        public UnsafeMultiAppendBuffer.Reader GetComponentReader() => _componentData.AsReader();

        public UnsafeMultiAppendBuffer.Reader GetLinksReader() => _bufferLinks.AsReader();

        public void CopyLinksTo(BufferLink* ptr, int count) => _bufferLinks.Copy(ptr, count * UnsafeUtility.SizeOf<BufferLink>());

        public void CopyBufferTo(byte* ptr, int sizeBytes)
        {
            _bufferData.Copy(ptr, sizeBytes);
        }

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

        //public EventQueue<T> AsQueue<T>() where T : struct
        //    => UnsafeUtilityEx.AsRef<EventQueue<T>>(UnsafeUtility.AddressOf(ref this));

        public T Cast<T>() where T : struct => UnsafeUtilityEx.AsRef<T>(UnsafeUtility.AddressOf(ref this));

        //public EventQueue<TComponent, TBufferData> Cast<TComponent, TBufferData>() 
        //    where TComponent : struct, IComponentData
        //    where TBufferData : struct, IBufferElementData
        //    => UnsafeUtilityEx.AsRef<EventQueue<TComponent, TBufferData>>(UnsafeUtility.AddressOf(ref this));
    }

}