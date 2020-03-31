using System.Reflection;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

namespace Vella.Events.Extensions
{
    public unsafe class ToStringFormatCache<T> where T : struct
    {
        // ReSharper disable StaticMemberInGenericType
        private static readonly FieldInfo[] _fields;
        private static readonly string[] _fieldNames;
        private static readonly int[] _fieldOffsets;
        private static readonly string _seed;
        private const string Seperator = "=";
        private const string Whitespace = " ";
        // ReSharper restore StaticMemberInGenericType

        static ToStringFormatCache()
        {
            _fields = typeof(T).GetFields();
            _fieldNames = new string[_fields.Length];
            _fieldOffsets = new int[_fields.Length];
            _seed = $"{typeof(T).Name}: ";

            for (var i = 0; i < _fields.Length; i++)
            {
                _fieldNames[i] = _fields[i].Name;
                _fieldOffsets[i] = UnsafeUtility.GetFieldOffset(_fields[i]);
            }
        }

        public static string GetToString(T instance)
        {
            var result = _seed;
            for (int i = 0; i < _fieldNames.Length; i++)
            {
                result += _fieldNames[i];
                result += Seperator;
                result += _fields[i].GetValue(instance);
                result += Whitespace;
            }
            return result;
        }
    }

    public static class ToStringExtensions
    {
        [BurstDiscard]
        public static string AutoToString<T>(this T obj) where T : struct
        {
            return ToStringFormatCache<T>.GetToString(obj);
        }
    }

}
