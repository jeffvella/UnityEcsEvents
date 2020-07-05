using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Vella.Tests.Data;

namespace Vella.Tests.Helpers
{

    public unsafe struct NativeArrayBuilder<T> : IDisposable, IEnumerable<T> where T : unmanaged
    {
        public NativeList<T> List;

        public static implicit operator NativeList<T>(NativeArrayBuilder<T> arr) => arr.List;

        public static implicit operator NativeArray<T>(NativeArrayBuilder<T> arr) => arr.List;

        public NativeArrayBuilder(Allocator allocator = Allocator.Temp)
        {
            List = new NativeList<T>(allocator);
        }

        public void Add(T item)
        {
            if (!List.IsCreated)
                List = new NativeList<T>(Allocator.Temp);
            List.Add(item);
        }

        public void Dispose() => List.Dispose();

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < List.Length; i++)
                yield return List[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
