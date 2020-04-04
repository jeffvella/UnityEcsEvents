using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System;

namespace Vella.Events.Extensions
{
    public unsafe static class UnsafeExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool IsFlagSet<T>(this T flags, T flag) where T : unmanaged, Enum
        {
            return (*(int*)&flags & *(int*)&flag) != 0;
        }

        /// <summary>
        /// A faster alternative to using NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray
        /// but has no safetychecks whatsoever.
        /// </summary>
        /// <typeparam name="T">the element type</typeparam>
        /// <param name="list">the input list of elements</param>
        /// <param name="safety">a safety to use for the new array (the input safety is not easily accessible)</param> 
        /// <returns></returns>
        public static NativeArray<T> ToNativeArray<T>(this NativeList<T> list, ref AtomicSafetyHandle safety) where T : struct
        {
            UnsafeNativeArray result = default;
            var writer = list.AsParallelWriter();
            result.m_Buffer = list.GetUnsafePtr();
            result.m_AllocatorLabel = writer.ListData->Allocator;
            result.m_Length = writer.ListData->Length;
            result.m_MaxIndex = writer.ListData->Length - 1;
            result.m_Safety = safety;
            return UnsafeUtilityEx.AsRef<NativeArray<T>>(UnsafeUtility.AddressOf(ref result));
        }

        public static NativeArray<T> ToNativeArray<T>(this UnsafeList<T> list) where T : unmanaged
        {
            UnsafeNativeArray result = default;
            result.m_Buffer = list.Ptr;
            result.m_AllocatorLabel = list.Allocator;
            result.m_Length = list.Length;
            result.m_MaxIndex = list.Length - 1;
            result.m_Safety = AtomicSafetyHandle.Create();
            return UnsafeUtilityEx.AsRef<NativeArray<T>>(UnsafeUtility.AddressOf(ref result));
        }

        public static UnsafeNativeArray ToUnsafeNativeArray<T>(this UnsafeList<T> list) where T : unmanaged
        {
            UnsafeNativeArray result = default;
            result.m_Buffer = list.Ptr;
            result.m_AllocatorLabel = list.Allocator;
            result.m_Length = list.Length;
            result.m_MaxIndex = list.Length - 1;
            result.m_Safety = AtomicSafetyHandle.Create();
            return result;
        }

        public static UnsafeNativeArray ToUnsafeNativeArray<T>(this NativeList<T> list) where T : struct
        {
            UnsafeNativeArray result = default;
            var writer = list.AsParallelWriter();
            result.m_Buffer = list.GetUnsafePtr();
            result.m_AllocatorLabel = writer.ListData->Allocator;
            result.m_Length = writer.ListData->Length;
            result.m_MaxIndex = writer.ListData->Length - 1;
            result.m_Safety = AtomicSafetyHandle.Create();
            return result;
        }

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetComponentOffsetFromChunkPtr<T>(this EntityManager em, Entity entity) where T : struct, IComponentData
        {
            return GetComponentOffsetFromChunkPtr(em, em.GetChunk(entity), TypeManager.GetTypeIndex<T>());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetComponentOffsetFromChunkPtr(this EntityManager em, Entity entity, ComponentType component)
        {
            return GetComponentOffsetFromChunkPtr(em, em.GetChunk(entity), component.TypeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetComponentOffsetFromChunkPtr(this EntityManager em, Entity entity, int typeIndex)
        {
            return GetComponentOffsetFromChunkPtr(em, em.GetChunk(entity), typeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetComponentOffsetFromChunkPtr(this EntityManager em, ArchetypeChunk chunk, ComponentType component)
        {
            return GetComponentOffsetFromChunkPtr(em, chunk, component.TypeIndex);
        }

        private static int GetComponentOffsetFromChunkPtr(this EntityManager em, ArchetypeChunk chunk, int typeIndex)
        {
            byte* chunkPtr = *(byte**)&chunk;
            var tmp = em.GetArchetypeChunkComponentType<ChunkHeader>(false);
            UnsafeUtility.CopyStructureToPtr(ref typeIndex, UnsafeUtility.AddressOf(ref tmp));
            return (int)((byte*)chunk.GetNativeArray(tmp).GetUnsafeReadOnlyPtr() - chunkPtr);
        }
    }

// CS0649: Field is never assigned to, and will always have its default value 
#pragma warning disable CS0649

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

#pragma warning restore CS0649
}

