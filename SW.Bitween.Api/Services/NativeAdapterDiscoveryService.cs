#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SW.Bitween.NativeAdapters;
using SW.PrimitiveTypes;

namespace SW.Bitween
{
    public class NativeAdapterDiscoveryService(
        IEnumerable<INativeInfolinkHandler> nativeHandlers,
        IEnumerable<INativeInfolinkMapper> nativeMappers,
        IEnumerable<INativeInfolinkReceiver> nativeReceivers,
        IEnumerable<INativeInfolinkValidator> nativeValidators,
        IEnumerable<INativeAdapter> nativeAdapters)
    {
        public const string NativePrefix = "native";
        public Dictionary<string, StartupValue> GetStartupValues(string adapterId)
        {
            var result = new Dictionary<string, StartupValue>();

            var adapter = nativeAdapters.FirstOrDefault(a => a.GetType().Name.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
            if (adapter == null)
                return result;

            var properties = adapter.StartupValuesType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var value = new StartupValue
                {
                    Type = prop.PropertyType.Name,
                    Optional = prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>() == null &&
                               (IsNullableType(prop.PropertyType) || GetDefaultValue(prop) != null),
                    Default = GetDefaultValue(prop),
                    Private = prop.GetCustomAttribute<SecureAttribute>() != null,
                    Description = prop.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description
                };

                result[prop.Name] = value;

            }

            return result;
        }
        public Dictionary<string, string> GetExpectedStartupValues(string adapterId)
        {
            var result = new Dictionary<string, string>();

            var adapter = nativeAdapters.FirstOrDefault(a => a.GetType().Name.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
            if (adapter == null)
                return result;

            var properties = adapter.StartupValuesType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var defaultValue = GetDefaultValue(prop);
                var hasRequiredAttribute =
                    prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>() != null;
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


        public INativeInfolinkHandler GetNativeHandler(string adapterId, Dictionary<string, string> settings)
        {
            var result =
                nativeHandlers.FirstOrDefault(a => a.Name.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
            if (result != null)
            {
                result.InitializeStartupValues(settings);
            }

            return result;
        }

        public INativeInfolinkMapper GetNativeMapper(string adapterId, Dictionary<string, string> settings)
        {
            var result =
                nativeMappers.FirstOrDefault(a => a.GetType().Name.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
            if (result != null)
            {
                result.InitializeStartupValues(settings);
            }

            return result;
        }

        public INativeInfolinkReceiver GetNativeReceiver(string adapterId, IDictionary<string, string> settings)
        {
            var result =
                nativeReceivers.FirstOrDefault(a => a.GetType().Name.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
            if (result != null)
            {
                result.InitializeStartupValues(settings);
            }

            return result;
        }

        public INativeInfolinkValidator GetNativeValidator(string adapterId, IDictionary<string, string> settings)
        {
            var result =
                nativeValidators.FirstOrDefault(a => a.GetType().Name.Equals(adapterId, StringComparison.OrdinalIgnoreCase));
            if (result != null)
            {
                result.InitializeStartupValues(settings);
            }

            return result;
        }

        public List<string> GetNativeAdapters(string? type)
        {
            List<INativeAdapter> adapters;

            switch (type?.ToLower())
            {
                case "handlers":
                    adapters = nativeHandlers.Cast<INativeAdapter>().ToList();
                    break;
                case "receivers":
                    adapters = nativeReceivers.Cast<INativeAdapter>().ToList();
                    break;
                case "validators":
                    adapters = nativeValidators.Cast<INativeAdapter>().ToList();
                    break;
                case "mappers":
                    adapters = nativeMappers.Cast<INativeAdapter>().ToList();
                    break;
                case null:
                    adapters = nativeAdapters.ToList();
                    break;
                default:
                    return new List<string>();
            }

            return adapters.Select(a => a.GetType().Name).ToList();
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