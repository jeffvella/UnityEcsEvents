using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;

namespace Vella.Events
{
    /// <summary>
    /// A collection of <see cref="UnsafeAppendBuffer"/> intended to allow one buffer per thread.
    /// </summary>
    public unsafe struct UnsafeMultiAppendBuffer
    {
        public const int DefaultThreadIndex = -1;
        private UnsafeAppendBuffer* _data;
        private Allocator _allocator;

        public UnsafeMultiAppendBuffer(Allocator allocator)
        {
            _allocator = allocator;

            var bufferSize = UnsafeUtility.SizeOf<UnsafeAppendBuffer>();
            var bufferCount = JobsUtility.MaxJobThreadCount + 1;
            var allocationSize = bufferSize * bufferCount;
            var initialBufferCapacityBytes = 1024;

            var ptr = (byte*)UnsafeUtility.Malloc(allocationSize, UnsafeUtility.AlignOf<int>(), Allocator.Persistent);
            UnsafeUtility.MemClear(ptr, allocationSize);

            for (int i = 0; i < bufferCount; i++)
            {
                var bufferPtr = (UnsafeAppendBuffer*)(ptr + bufferSize * i);
                var buffer = new UnsafeAppendBuffer(initialBufferCapacityBytes, UnsafeUtility.AlignOf<int>(), Allocator.Persistent);
                UnsafeUtility.CopyStructureToPtr(ref buffer, bufferPtr);
            }

            _data = (UnsafeAppendBuffer*)ptr;
        }

        /// <summary>
        /// Adds data to the collection.
        /// </summary>
        /// <typeparam name="T">the type of the item being added</typeparam>
        /// <param name="threadIndex">the currently used thread index (or -1 for a shared channel)</param>
        /// <param name="item">the item to be added</param>
        public void Enqueue<T>(int threadIndex, T item) where T : struct, IComponentData
        {
            ref var buffer = ref GetBuffer(threadIndex);
            buffer.Add(item);
        }


        /// <summary>
        /// Retrieve buffer for a specific thread index.
        /// </summary>
        /// <param name="threadIndex"></param>
        /// <returns></returns>
        public ref UnsafeAppendBuffer GetBuffer(int threadIndex)
        {
            // All indexes are offset by +1; Unspecified ThreadIndex 
            // (main thread without explicitly checking for ThreadId) 
            // should use first index by providing threadIndex of -1;

            return ref UnsafeUtilityEx.ArrayElementAsRef<UnsafeAppendBuffer>(_data, threadIndex + 1);
        }

        /// <summary>
        /// Calculates the current total size of data that has been added.
        /// </summary>
        public int Size()
        {
            //return GetBuffer(DefaultThreadIndex).Size;

            var totalSize = 0;
            for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
            {
                ref var buffer = ref GetBuffer(i);
                totalSize += buffer.Size;
            }
            return totalSize;
        }

        /// <summary>
        /// Combines the data from all internal buffers and writes it to a single destination.
        /// </summary>
        /// <param name="destinationPtr">a location to be written to</param>
        /// <param name="maxSizeBytes">maximum amount of data (in bytes) to be written to <paramref name="destinationPtr"/></param>
        public void Copy(void* destinationPtr, int maxSizeBytes)
        {
            if (destinationPtr == null)
                throw new NullReferenceException();

            int sum = 0;
            byte* pos = (byte*)destinationPtr;
            for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
            {
                ref var buffer = ref GetBuffer(i);
                if (buffer.Size > 0)
                {
                    sum += buffer.Size;
                    if (sum > maxSizeBytes)
                        throw new Exception("Attempt to write data beyond the target allocation");

                    UnsafeUtility.MemCpy(pos, buffer.Ptr, buffer.Size);
                    pos += buffer.Size;
                }
            }
        }

        public void Dispose()
        {
            for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
            {
                GetBuffer(i).Dispose();
            }
            UnsafeUtility.Free(_data, _allocator);
        }

        public void Clear()
        {
            for (int i = -1; i < JobsUtility.MaxJobThreadCount + 1; i++)
            {
                GetBuffer(i).Reset();
            }
        }
    }

}