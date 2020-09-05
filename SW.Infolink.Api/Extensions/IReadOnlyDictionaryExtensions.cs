using SW.PrimitiveTypes;
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

        public static ICollection<KeyAndValue> ToKeyAndValueCollection<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict)
        {
            return dict.Select(kvp => new KeyAndValue
            {
                Key = kvp.Key.ToString(),
                Value = kvp.Value?.ToString()
            }).ToList();
        }

        public static Dictionary<string, string> ToDictionary(this ICollection<KeyAndValue> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IDictionary<TKey, TValue> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}
