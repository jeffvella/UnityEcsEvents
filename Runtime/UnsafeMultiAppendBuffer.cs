using System;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Vella.Events
{
    /// <summary>
    /// A collection of <see cref="UnsafeAppendBuffer"/> intended to allow one buffer per thread.
    /// </summary>
    public unsafe struct UnsafeMultiAppendBuffer
    {
        public const int DefaultThreadIndex = -1;
        private UnsafeAppendBuffer* _data;
        public readonly Allocator Allocator;

        public UnsafeMultiAppendBuffer(Allocator allocator)
        {
            Allocator = allocator;

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
            var buffer = GetBuffer(threadIndex);
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
        /// <returns>the amount of data written in bytes</returns>
        public int Copy(void* destinationPtr, int maxSizeBytes)
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
                    var amountToWrite = math.min(maxSizeBytes, buffer.Size);
                    sum += amountToWrite;

                    if (sum > maxSizeBytes)
                        throw new Exception("Attempt to write data beyond the target allocation");

                    UnsafeUtility.MemCpy(pos, buffer.Ptr, amountToWrite);
                    pos += amountToWrite;
                }
            }

            return sum;
        }

        public Reader AsReader()
        {
            Reader reader;
            reader.Data = this;
            reader.WrittenTotal = 0;
            reader.WrittenFromIndex = 0;
            reader.Index = DefaultThreadIndex;
            return reader;
        }

        public struct Reader
        {
            public UnsafeMultiAppendBuffer Data;
            public int WrittenTotal;
            public int WrittenFromIndex;
            public int Index;

            /// <summary>
            /// Copies from the pool of data remaining to be read, to the provided destination.
            /// </summary>
            /// <param name="destinationPtr">where to write the data</param>
            /// <param name="maxSizeBytes">the maximum amount of data that can be written to <paramref name="destinationPtr"/> (in bytes)</param>
            /// <returns></returns>
            public int CopyTo(void* destinationPtr, int maxSizeBytes)
            {
                if (destinationPtr == null)
                    throw new NullReferenceException();

                // destinationPos
                byte* destPos = (byte*)destinationPtr;
                int sum = 0;

                for (; Index < JobsUtility.MaxJobThreadCount; Index++)
                {
                    ref var buffer = ref Data.GetBuffer(Index);
                    if (buffer.Size > 0)
                    {
                        var amountToWrite = math.min(maxSizeBytes, buffer.Size);

                        sum += amountToWrite;
                        if (sum > maxSizeBytes)
                            throw new Exception("Attempt to write data beyond the target allocation");

                        UnsafeUtility.MemCpy(destPos, buffer.Ptr + WrittenFromIndex, amountToWrite);

                        destPos += amountToWrite;

                        // Allow continutation, but clear it when each index is finished.
                        WrittenFromIndex += amountToWrite;
                        if (WrittenFromIndex >= buffer.Size)
                            WrittenFromIndex = 0;

                        WrittenTotal += amountToWrite;
                    }
                }

                return sum;
            }
        }

        public void Dispose()
        {
            for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
            {
                GetBuffer(i).Dispose();
            }
            UnsafeUtility.Free(_data, Allocator);
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