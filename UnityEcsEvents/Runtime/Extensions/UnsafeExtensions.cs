using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System;

namespace Vella.Events.Extensions
{
    public static unsafe class UnsafeExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool IsFlagSet<T>(this T flags, T flag) where T : unmanaged, Enum
        {
            return (*(int*)&flags & *(int*)&flag) != 0;
        }
    }
}

//// CS0649: Field is never assigned to, and will always have its default value 
//#pragma warning disable CS0649

//    [StructLayout(LayoutKind.Explicit, Size = 16)]
//    public unsafe struct ArchetypeChunkProxy
//    {
//        [FieldOffset(0)]
//        public void* m_Chunk;

//        [FieldOffset(8)]
//        public void* entityComponentStore;
//    }

//    public unsafe struct EntityArchetypeProxy
//    {
//        [NativeDisableUnsafePtrRestriction]
//        public ArchetypeProxy* Archetype;

//        [NativeDisableUnsafePtrRestriction]
//        public void* _DebugComponentStore;
//    }

//    public unsafe struct ArchetypeProxy
//    {
//        public ArchetypeChunkDataProxy Chunks;
//    }

//    public struct ArchetypeChunkDataProxy
//    {
//        public unsafe void** p;

//        public unsafe int* data;

//        public int Capacity;

//        public int Count;
//    }

//#pragma warning restore CS0649
//}

