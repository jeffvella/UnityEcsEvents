using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System;

namespace Vella.Events
{
    public unsafe static class UnsafeExtensions
    {
        //public static T* Add<T>(this UnsafeAppendBuffer buffer, T* array, int length) where T : unmanaged
        //{
        //    var sizeBytes = length * sizeof(T);
        //    buffer.SetCapacity(buffer.Length + sizeBytes);
        //    var startPtr = buffer.Ptr + buffer.Length;
        //    UnsafeUtility.MemCpy(startPtr, array, sizeBytes);
        //    buffer.Length += sizeBytes;
        //    return (T*)startPtr;
        //}

        //public static UnsafeList* AddUnsafeList<T>(this UnsafeAppendBuffer buffer, T* array, int length) where T : unmanaged
        //{
        //    //var dstStartLengthBytes = buffer.Length;
        //    //var srcLengthBytes = length * sizeof(T);
        //    //buffer.ResizeUninitialized(dstStartLengthBytes + sizeof(UnsafeList) + srcLengthBytes);
        //    //var arrStartPtr = buffer.Ptr + dstStartLengthBytes + sizeof(UnsafeList);
        //    //buffer.Add(new UnsafeList(arrStartPtr, srcLengthBytes));
        //    //buffer.Add(array, srcLengthBytes);
        //    //var resultPtr = (UnsafeList*)buffer.Ptr + dstStartLengthBytes;
        //    //var resultDebug = UnsafeUtilityEx.AsRef<UnsafeList<T>>(resultPtr);
        //    //return resultPtr;

        //    //// Add an UnsafeList and an array of data directly following it.

        //    //buffer.SetCapacity(buffer.Length + UnsafeUtility.SizeOf<UnsafeList<T>>() + length * UnsafeUtility.SizeOf<T>());
        //    //var list = new UnsafeList<T>(array, length);
        //    //var arrayOffset = buffer.Ptr + buffer.Length;
        //    //buffer.AddArray<T>(array, length);
        //    //buffer.AsReader().read
        //    //var listOffset = buffer.Ptr + buffer.Length - UnsafeUtility.SizeOf<int>();
        //    //buffer.Add(list);

        //    var listSizeBytes = UnsafeUtility.SizeOf<UnsafeList>();
        //    var arraySizeBytes = length * UnsafeUtility.SizeOf<T>();
        //    buffer.SetCapacity(buffer.Length + listSizeBytes + arraySizeBytes);

        //    var listStartPtr = buffer.Ptr + buffer.Length;
        //    var dataStartPtr = listStartPtr + listSizeBytes;

        //    //Debug.Assert(sizeof(UnsafeList) != UnsafeUtility.SizeOf<UnsafeList>());

        //    //if(sizeof(UnsafeList) == UnsafeUtility.SizeOf<UnsafeList>())
        //    //    throw new InvalidOperationException();

        //    UnsafeList list;
        //    list.Allocator = Allocator.None;
        //    list.Length = length;
        //    list.Capacity = length;
        //    list.Ptr = dataStartPtr;

        //    UnsafeUtility.MemCpy(listStartPtr, &list, listSizeBytes);
        //    UnsafeUtility.MemCpy(dataStartPtr, array, arraySizeBytes);

        //    buffer.Length += listSizeBytes + arraySizeBytes;

        //    if (buffer.Ptr == null)
        //        throw new NullReferenceException();

        //    if (listStartPtr == null)
        //        throw new NullReferenceException();

        //    if (dataStartPtr == null)
        //        throw new NullReferenceException();

        //    var data = (UnsafeAppendBuffer*)dataStartPtr;

        //    UnsafeList* result = (UnsafeList*)listStartPtr;
        //    return result;
        //}

        public static void Swap<T>(this UnsafeList<T> instance, int sourceIndex, int destinationIndex) where T : unmanaged
        {
            T* ptr = instance.Ptr;
            T tmp = ptr[sourceIndex];
            ptr[sourceIndex] = ptr[destinationIndex];
            ptr[destinationIndex] = tmp;
        }

        public static void Swap<T>(this UnsafeList instance, int sourceIndex, int destinationIndex) where T : unmanaged
        {
            T* ptr = (T*)instance.Ptr;
            T tmp = ptr[sourceIndex];
            ptr[sourceIndex] = ptr[destinationIndex];
            ptr[destinationIndex] = tmp;
        }

        /// <summary>
        /// Copies the current chunks from an <see cref="EntityArchetype"/> to a <see cref="NativeArray{T}"/>
        /// </summary>
        public static void CopyChunksTo(this EntityArchetype archetype, NativeArray<ArchetypeChunk> destination)
        {
            var archetypeProxy = *(EntityArchetypeProxy*)&archetype;
            var chunkData = archetypeProxy.Archetype->Chunks;
            var destinationPtr = (ArchetypeChunkProxy*)destination.GetUnsafePtr();

            for (int i = 0; i < chunkData.Count; i++)
            {
                ArchetypeChunkProxy chunk;
                chunk.m_Chunk = chunkData.p[i];
                chunk.entityComponentStore = archetypeProxy._DebugComponentStore;
                destinationPtr[i] = chunk;
            }
        }

        ///// <summary>
        ///// Copies a specified amount of chunks from an <see cref="EntityArchetype"/> to a <see cref="NativeArray{T}"/>
        ///// </summary>
        //public static void CopyChunksTo(this EntityArchetype archetype, NativeArray<ArchetypeChunk> destination, int count)
        //{
        //    var archetypeProxy = *(EntityArchetypeProxy*)&archetype;
        //    var chunkData = archetypeProxy.Archetype->Chunks;
        //    var destinationPtr = (ArchetypeChunkProxy*)destination.GetUnsafePtr();

        //    for (int i = 0; i < count; i++)
        //    {
        //        ArchetypeChunkProxy chunk;
        //        chunk.m_Chunk = chunkData.p[i];
        //        chunk.entityComponentStore = archetypeProxy._DebugComponentStore;
        //        destinationPtr[i] = chunk;
        //    }
        //}

        /// <summary>
        /// Retrieves the current chunks from an <see cref="EntityArchetype"/>.
        /// </summary>
        public static ArchetypeChunk FirstChunk(this EntityArchetype archetype)
        {
            var archetypeProxy = *(EntityArchetypeProxy*)&archetype;
            var chunkData = archetypeProxy.Archetype->Chunks;
            ArchetypeChunkProxy chunk;
            chunk.m_Chunk = chunkData.p[0];
            chunk.entityComponentStore = archetypeProxy._DebugComponentStore;
            return *(ArchetypeChunk*)&chunk;

        }

        /// <summary>
        /// Retrieves the current chunks from an <see cref="EntityArchetype"/>.
        /// </summary>
        public static unsafe void CopyChunksTo(this EntityArchetype archetype, void* destination, int destinationOffset)
        {
            var archetypeProxy = *(EntityArchetypeProxy*)&archetype;
            var chunkData = archetypeProxy.Archetype->Chunks;
            var destinationPtr = (ArchetypeChunkProxy*)((byte*)destination + destinationOffset * sizeof(ArchetypeChunk));

            for (int i = 0; i < chunkData.Count; i++)
            {
                ArchetypeChunkProxy chunk;
                chunk.m_Chunk = chunkData.p[i];
                chunk.entityComponentStore = archetypeProxy._DebugComponentStore;
                destinationPtr[i] = chunk;
            }
        }

        /// <summary>
        /// Retrieves the current chunks from an <see cref="EntityArchetype"/>.
        /// </summary>
        public static unsafe void CopyChunksTo(this EntityArchetype archetype, ArchetypeChunk* destinationPtr)
        {
            var entityArchetypeProxy = *(EntityArchetypeProxy*)&archetype;
            var chunkData = entityArchetypeProxy.Archetype->Chunks;

            for (int i = 0; i < chunkData.Count; i++)
            {
                ArchetypeChunkProxy chunk;
                chunk.m_Chunk = chunkData.p[i];
                chunk.entityComponentStore = entityArchetypeProxy._DebugComponentStore;
                UnsafeUtility.CopyStructureToPtr(ref chunk, destinationPtr + i * sizeof(ArchetypeChunk));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void* GetComponentDataStorePtr(this ArchetypeChunk chunk)
        {
            return ((ArchetypeChunkProxy*)&chunk)->entityComponentStore;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void* GetChunkPtr(this ArchetypeChunk chunk)
        {
            return ((ArchetypeChunkProxy*)&chunk)->m_Chunk;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void* GetArchetypePtr(this EntityArchetype archetype)
        {
            return (void*)(*(EntityArchetypeProxy*)&archetype).Archetype;
        }

        private static int GetComponentOffsetFromChunkPtr(this EntityManager em, ArchetypeChunk chunk, ComponentType component)
        {
            byte* chunkPtr = *(byte**)&chunk;
            var tmp = em.GetArchetypeChunkComponentType<ChunkHeader>(false);
            UnsafeUtility.CopyStructureToPtr(ref component.TypeIndex, UnsafeUtility.AddressOf(ref tmp));
            return (int)((byte*)chunk.GetNativeArray(tmp).GetUnsafeReadOnlyPtr() - chunkPtr);
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        public unsafe struct ArchetypeChunkProxy
        {
            [FieldOffset(0)]
            public void* m_Chunk;

            [FieldOffset(8)]
            public void* entityComponentStore;
        }

        public unsafe struct EntityArchetypeProxy
        {
            [NativeDisableUnsafePtrRestriction]
            public ArchetypeProxy* Archetype;

            [NativeDisableUnsafePtrRestriction]
            public void* _DebugComponentStore;
        }

        public unsafe struct ArchetypeProxy
        {
            public ArchetypeChunkDataProxy Chunks;
        }

        public struct ArchetypeChunkDataProxy
        {
            public unsafe void** p;

            public unsafe int* data;

            public int Capacity;

            public int Count;
        }


    }
}

