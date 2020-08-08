using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SW.Infolink
{
    public static class IReadOnlyDictionaryExtensions
    {
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}
