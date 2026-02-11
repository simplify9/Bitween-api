using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween
{
    public class NativeAdapterDiscoveryService
    {
        private readonly Dictionary<string, List<NativeAdapterInfo>> _adaptersCache;

        public NativeAdapterDiscoveryService()
        {
            _adaptersCache = new Dictionary<string, List<NativeAdapterInfo>>();
            DiscoverNativeAdapters();
        }

        private void DiscoverNativeAdapters()
        {
            var assemblies = new List<Assembly>() { typeof(DictionaryConverter).Assembly };
        


        foreach (var assembly in assemblies)
            {
                try
                {
                    var types = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract);

                    foreach (var type in types)
                    {
                        if (typeof(IInfolinkHandler).IsAssignableFrom(type))
                        {
                            AddAdapter("handlers", type);
                        }
                        else if (typeof(IInfolinkValidator).IsAssignableFrom(type))
                        {
                            AddAdapter("validators", type);
                        }
                        else if (typeof(IInfolinkReceiver).IsAssignableFrom(type))
                        {
                            AddAdapter("receivers", type);
                        }
                    }
                }
                catch
                {
                    // Skip assemblies that can't be loaded or scanned
                }
            }
        }

        private void AddAdapter(string category, Type type)
        {
            if (!_adaptersCache.ContainsKey(category))
            {
                _adaptersCache[category] = new List<NativeAdapterInfo>();
            }

            var adapterName = type.Name;//.Replace("Handler", "").Replace("Mapper", "")
                //.Replace("Validator", "").Replace("Receiver", "").ToLower();

            _adaptersCache[category].Add(new NativeAdapterInfo
            {
                Key = $"native.{adapterName}",
                Name = type.Name,
                Type = type,
                Category = category
            });
        }

        public IEnumerable<string> GetNativeAdapters(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return _adaptersCache.Values.SelectMany(v => v).Select(a => a.Key);
            }

            var category = prefix.ToLower().TrimStart('.');
            
            if (_adaptersCache.TryGetValue(category, out var adapters))
            {
                return adapters.Select(a => a.Key);
            }

            return Enumerable.Empty<string>();
        }

        public NativeAdapterInfo GetNativeAdapterInfo(string adapterId)
        {
            return _adaptersCache.Values
                .SelectMany(v => v)
                .FirstOrDefault(a => a.Key.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
        }

        public Dictionary<string, string> GetNativeAdapterProperties(string adapterId)
        {
            var adapterInfo = GetNativeAdapterInfo(adapterId);
            if (adapterInfo == null)
                return new Dictionary<string, string>();

            var result = new Dictionary<string, string>();

            // Get constructor parameters
            var constructor = adapterInfo.Type.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length > 0);

            if (constructor == null)
                return result;

            // Get the first parameter type (input model)
            var inputParameter = constructor.GetParameters().FirstOrDefault();
            if (inputParameter == null)
                return result;

            var inputType = inputParameter.ParameterType;

            // Get all properties from the input model
            var properties = inputType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var defaultValue = GetDefaultValue(prop);
                var hasRequiredAttribute = prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>() != null;
                var isRequired = hasRequiredAttribute || (!IsNullableType(prop.PropertyType) && defaultValue == null);
                
                if (isRequired)
                {
                    result[prop.Name] = $"{prop.Name} *";
                }
                else
                {
                    result[prop.Name] = $"{prop.Name} ({defaultValue ?? "null"})";
                }
            }

            return result;
        }

        private string? GetDefaultValue(PropertyInfo property)
        {
            // Try to get default value from DefaultValueAttribute if it exists
            var defaultAttr = property.GetCustomAttribute<System.ComponentModel.DefaultValueAttribute>();
            if (defaultAttr != null)
                return defaultAttr.Value?.ToString();

            // For value types, return their default
            if (property.PropertyType.IsValueType)
                return Activator.CreateInstance(property.PropertyType)?.ToString();

            return null;
        }

        private bool IsNullableType(Type type)
        {
            return !type.IsValueType || 
                   Nullable.GetUnderlyingType(type) != null ||
                   (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
        }
    }

    public class NativeAdapterInfo
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Type Type { get; set; } = null!;
        public string Category { get; set; } = string.Empty;
    }
}
