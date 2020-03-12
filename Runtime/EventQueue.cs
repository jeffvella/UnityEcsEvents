using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;

namespace Vella.Events
{
    /// <summary>
    /// A collection that allows systems/jobs to schedule event components to be created later in the frame.
    /// <para>
    /// Event components are attached to new entities by the <see cref="EntityEventSystem'"/>
    /// and exist for one frame only (until <see cref="EntityEventSystem"/>.OnUpdate() runs again).
    /// </para>
    /// This is intended to be passed into jobs, where it will be injected with thread index 
    /// and then leverage the corrisponding queue dedicated to that thread.
    /// </summary>
    /// <typeparam name="T">type of event</typeparam>
    public unsafe struct EventQueue<T> where T : struct, IComponentData
    {
        private UnsafeMultiAppendBuffer _data;

        [NativeSetThreadIndex]
        private int _threadIndex;

        public void Enqueue(T item) => _data.GetBuffer(_threadIndex).Add(item);
    }

    /// <summary>
    /// Wraps management of a queue for storing up components
    /// from multiple sources (c# events/systems/jobs/threads).
    /// This untyped version is a union with <see cref="EventQueue{T}"/>
    /// </summary>
    internal unsafe struct EventQueue
    {
        private UnsafeMultiAppendBuffer _data;

        [NativeSetThreadIndex]
        private int _threadIndex;
        private int _itemSize;
        private int _cachedCount;

        public EventQueue(int itemSize, Allocator allocator)
        {
            _data = new UnsafeMultiAppendBuffer(allocator);
            _threadIndex = UnsafeMultiAppendBuffer.DefaultThreadIndex;
            _itemSize = itemSize;
            _cachedCount = 0;
        }

        public int CachedCount => _cachedCount;

        public int Count() => _cachedCount = _data.Size() / _itemSize;

        public void CopyEventsTo(byte* ptr, int sizeBytes) => _data.Copy(ptr, sizeBytes);

        public void Clear() => _data.Clear();

        public void Dispose() => _data.Dispose();

        public EventQueue<T> Cast<T>() where T : struct, IComponentData
            => UnsafeUtilityEx.AsRef<EventQueue<T>>(UnsafeUtility.AddressOf(ref this));
    }

}