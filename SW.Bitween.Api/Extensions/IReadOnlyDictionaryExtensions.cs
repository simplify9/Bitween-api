using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SW.Bitween
{
    public static class IReadOnlyDictionaryExtensions
    {
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict)
        {
            return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public static ICollection<KeyAndValue> ToKeyAndValueCollection<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> dict)
        {
            // A null column is "no entries", not a failure. Rows the domain creates always have
            // one, but a row inserted any other way took down the whole list response rather
            // than the single row that was missing it.
            if (dict == null) return new List<KeyAndValue>();

            return dict.Select(kvp => new KeyAndValue
            {
                Key = kvp.Key.ToString(),
                Value = kvp.Value?.ToString()
            }).ToList();
        }

        /// <summary>
        /// An exchange's promoted values in the order its information type lists the properties — the
        /// order they're shown in, and the first one names an archived exchange. The stored values can't
        /// be trusted to keep it (Postgres re-sorts a jsonb object's keys), and the type's order can
        /// change after the exchange ran. Values for properties the type no longer has go last.
        /// </summary>
        public static Dictionary<string, string> InDefinedOrder(this IReadOnlyDictionary<string, string> values,
            IReadOnlyDictionary<string, string> definition)
        {
            if (values == null) return null;

            var ordered = new Dictionary<string, string>();
            foreach (var name in definition?.Keys ?? Enumerable.Empty<string>())
                if (values.TryGetValue(name, out var value))
                    ordered[name] = value;
            foreach (var (name, value) in values)
                ordered.TryAdd(name, value);
            return ordered;
        }

        public static string SafeGetValue(this IReadOnlyDictionary<string, string> dict, string key)
        {
            return dict.TryGetValue(key, out var value) ? value : "";
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