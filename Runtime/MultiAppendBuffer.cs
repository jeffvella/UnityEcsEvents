using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Vella.Events
{
    /// <summary>
    /// A collection of <see cref="UnsafeAppendBuffer"/> intended to allow one buffer per thread.
    /// </summary>
    [DebuggerDisplay("IsEmpty={IsEmpty}")]
    public unsafe struct MultiAppendBuffer
    {
        public const int DefaultThreadIndex = -1;
        public const int MaxThreadIndex = JobsUtility.MaxJobThreadCount - 1;
        public const int MinThreadIndex = DefaultThreadIndex;

        [NativeDisableUnsafePtrRestriction]
        public UnsafeAppendBuffer* Ptr;

        internal byte* BaseAddress => (byte*)Ptr - sizeof(Allocator);
        public Allocator Allocator => *(Allocator*)BaseAddress;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsInvalidThreadIndex(int index) => index < MinThreadIndex || index > MaxThreadIndex;

        public bool IsEmpty => Size() == 0;

        public MultiAppendBuffer(Allocator allocator, int initialCapacityPerThread = JobsUtility.CacheLineSize)
        {
            var bufferSize = UnsafeUtility.SizeOf<UnsafeAppendBuffer>();
            var bufferCount = JobsUtility.MaxJobThreadCount + 1;
            var allocationSize = bufferSize * bufferCount;
            var initialBufferCapacityBytes = initialCapacityPerThread;

            var ptr = (byte*)UnsafeUtility.Malloc(allocationSize, UnsafeUtility.AlignOf<int>(), allocator);
            UnsafeUtility.MemClear(ptr, allocationSize);
            UnsafeUtility.CopyStructureToPtr(ref allocator, ptr);

            var dataStartPtr = ptr + sizeof(Allocator);
            for (int i = 0; i < bufferCount; i++)
            {
                var bufferPtr = (UnsafeAppendBuffer*)(dataStartPtr + bufferSize * i);
                var buffer = new UnsafeAppendBuffer(initialBufferCapacityBytes, UnsafeUtility.AlignOf<int>(), allocator);
                UnsafeUtility.CopyStructureToPtr(ref buffer, bufferPtr);
            }
            Ptr = (UnsafeAppendBuffer*)dataStartPtr;
        }

        /// <summary>
        /// Adds data to the collection.
        /// </summary>
        /// <typeparam name="T">the type of the item being added</typeparam>
        /// <param name="threadIndex">the currently used thread index (or -1 for a shared channel)</param>
        /// <param name="item">the item to be added</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Enqueue<T>(int threadIndex, T item) where T : struct, IComponentData
        {
            GetBuffer(threadIndex).Add(item);
        }

        /// <summary>
        /// Retrieve buffer for a specific thread index.
        /// </summary>
        /// <param name="threadIndex"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref UnsafeAppendBuffer GetBuffer(int threadIndex)
        {
            // All indexes are offset by +1; Unspecified ThreadIndex 
            // (main thread without explicitly checking for ThreadId) 
            // should use first index by providing threadIndex of -1;

            return ref UnsafeUtilityEx.ArrayElementAsRef<UnsafeAppendBuffer>(Ptr, threadIndex + 1);
        }

        /// <summary>
        /// Calculates the current total size of data that has been added.
        /// </summary>
        public int Size()
        {
            var totalSize = 0;
            for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
            {
                totalSize += GetBuffer(i).Length;
            }
            return totalSize;
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

        /// <summary>
        /// A reader instance lets you keep track of the current read position and therefore easily
        /// copy data to different destinations (e.g. chunks); each time continuing from where it left off.
        /// </summary>
        public struct Reader
        {
            public MultiAppendBuffer Data;
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

                byte* pos = (byte*)destinationPtr;
                int bytesWritten = 0;

                for (; Index < JobsUtility.MaxJobThreadCount; Index++)
                {
                    ref var buffer = ref Data.GetBuffer(Index);
                    if (buffer.Length > 0)
                    {
                        var amountToWrite = math.min(maxSizeBytes, buffer.Length);

                        bytesWritten += amountToWrite;
                        if (bytesWritten > maxSizeBytes)
                            throw new Exception("Attempt to write data beyond the target allocation");

                        UnsafeUtility.MemCpy(pos, buffer.Ptr + WrittenFromIndex, amountToWrite);

                        pos += amountToWrite;

                        WrittenTotal += amountToWrite;
                        WrittenFromIndex += amountToWrite;

                        if (WrittenFromIndex >= buffer.Length)
                        {
                            WrittenFromIndex = 0;
                        }

                        if (maxSizeBytes <= buffer.Length)
                        {
                            return bytesWritten;
                        }
                    }
                }

                return bytesWritten;
            }
        }

        public void Dispose()
        {
            for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
            {
                GetBuffer(i).Dispose();
            }
            UnsafeUtility.Free(BaseAddress, Allocator);
        }

        public void Clear()
        {
            for (int i = -1; i < JobsUtility.MaxJobThreadCount; i++)
            {
                GetBuffer(i).Reset();
            }
        }
    }

}