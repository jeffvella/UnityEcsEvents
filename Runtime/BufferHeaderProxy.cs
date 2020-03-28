using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Vella.Events
{
    /// <summary>
    /// This is an exact copy of BufferHeader, except public.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct BufferHeaderProxy
    {
        public enum TrashMode
        {
            TrashOldData,
            RetainOldData
        }

        public const int kMinimumCapacity = 8;

        [FieldOffset(0)]
        public unsafe byte* Pointer;

        [FieldOffset(8)]
        public int Length;

        [FieldOffset(12)]
        public int Capacity;

        public unsafe static byte* GetElementPointer(BufferHeaderProxy* header)
        {
            if (header->Pointer != null)
            {
                return header->Pointer;
            }
            return (byte*)(header + 1);
        }

        public unsafe static void EnsureCapacity(BufferHeaderProxy* header, int count, int typeSize, int alignment, TrashMode trashMode, bool useMemoryInitPattern, byte memoryInitPattern)
        {
            if (count > header->Capacity)
            {
                int adjustedCount = math.max(8, math.max(2 * header->Capacity, count));
                SetCapacity(header, adjustedCount, typeSize, alignment, trashMode, useMemoryInitPattern, memoryInitPattern, 0);
            }
        }

        public unsafe static void SetCapacity(BufferHeaderProxy* header, int count, int typeSize, int alignment, TrashMode trashMode, bool useMemoryInitPattern, byte memoryInitPattern, int internalCapacity)
        {
            if (count == header->Capacity)
            {
                return;
            }
            long newSizeInBytes = (long)count * (long)typeSize;
            byte* oldData = GetElementPointer(header);
            byte* newData = (byte*)((count <= internalCapacity) ? (header + 1) : UnsafeUtility.Malloc(newSizeInBytes, alignment, Allocator.Persistent));
            if (oldData != newData)
            {
                if (useMemoryInitPattern)
                {
                    if (trashMode == TrashMode.RetainOldData)
                    {
                        int oldSizeInBytes = header->Capacity * typeSize;
                        long bytesToInitialize = newSizeInBytes - oldSizeInBytes;
                        if (bytesToInitialize > 0)
                        {
                            UnsafeUtility.MemSet(newData + oldSizeInBytes, memoryInitPattern, bytesToInitialize);
                        }
                    }
                    else
                    {
                        UnsafeUtility.MemSet(newData, memoryInitPattern, newSizeInBytes);
                    }
                }
                if (trashMode == TrashMode.RetainOldData)
                {
                    long bytesToCopy = math.min((long)header->Capacity, (long)count) * typeSize;
                    UnsafeUtility.MemCpy(newData, oldData, bytesToCopy);
                }
                if (header->Pointer != null)
                {
                    UnsafeUtility.Free(header->Pointer, Allocator.Persistent);
                }
            }
            header->Pointer = (byte*)(long)((newData == header + 1) ? ((IntPtr)(void*)null) : ((IntPtr)newData));
            header->Capacity = count;
        }

        public unsafe static void Assign(BufferHeaderProxy* header, byte* source, int count, int typeSize, int alignment, bool useMemoryInitPattern, byte memoryInitPattern)
        {
            EnsureCapacity(header, count, typeSize, alignment, TrashMode.TrashOldData, useMemoryInitPattern, memoryInitPattern);
            byte* elementPtr = GetElementPointer(header);
            UnsafeUtility.MemCpy(elementPtr, source, (long)typeSize * (long)count);
            header->Length = count;
        }
    }

}