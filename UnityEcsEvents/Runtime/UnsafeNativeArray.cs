//using System.Runtime.CompilerServices;
//using Unity.Collections;
//using Unity.Collections.LowLevel.Unsafe;

///// <summary>
///// An accessible proxy for NativeArray<T>.
///// </summary>
//public unsafe struct UnsafeNativeArray
//{
//    [NativeDisableUnsafePtrRestriction]
//    public unsafe void* m_Buffer;
//    public int m_Length;
//    public int m_MinIndex;
//    public int m_MaxIndex;
//    public AtomicSafetyHandle m_Safety;

//    [NativeSetClassTypeToNullOnSchedule]
//    public DisposeSentinel m_DisposeSentinel;
//    public Allocator m_AllocatorLabel;

//    [MethodImpl(MethodImplOptions.AggressiveInlining)]
//    public NativeArray<T> AsNativeArray<T>() where T : struct
//    {
//        return UnsafeUtilityEx.AsRef<NativeArray<T>>(UnsafeUtility.AddressOf(ref this));
//    }

//    /// <summary>
//    /// Creates a window of the underlying NativeArray, shifted to encapulate a specified region.
//    /// Useful for Entities API functions that require a NativeArray but won't accept a NativeSlice etc
//    /// </summary>
//    /// <typeparam name="T">the item type to use</typeparam>e
//    /// <param name="startIndex">new start index</param>
//    /// <param name="length">new item count</param>
//    /// <returns></returns>
//    public NativeArray<T> Slice<T>(int startIndex, int length) where T : struct
//    {
//        UnsafeNativeArray result = this;
//        result.m_Buffer = (byte*)m_Buffer + startIndex * UnsafeUtility.SizeOf<T>();
//        result.m_Length = length;
//        result.m_MaxIndex = length - 1;
//        return UnsafeUtilityEx.AsRef<NativeArray<T>>(UnsafeUtility.AddressOf(ref result));
//    }
//}
