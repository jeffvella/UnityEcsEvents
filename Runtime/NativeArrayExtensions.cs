using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Vella.Events
{
    public unsafe static class NativeArrayExtensions
    {
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
    }
}
