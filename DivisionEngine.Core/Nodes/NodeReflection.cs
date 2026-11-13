//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Collections.Concurrent;
using System.Reflection;

namespace DivisionEngine.Nodes
{
    /// <summary>
    /// Reflected access to one <see cref="NodePropertyAttribute"/> member on a node.
    /// </summary>
    public sealed class NodePropertyDescriptor
    {
        private readonly FieldInfo? fieldInfo;
        private readonly PropertyInfo? property;
        private readonly NodePropertyAttribute attribute;

        internal NodePropertyDescriptor(MemberInfo member, NodePropertyAttribute attribute)
        {
            Member = member;
            this.attribute = attribute;
            fieldInfo = member as FieldInfo;
            property = member as PropertyInfo;
            Name = member.Name;
            ValueType = fieldInfo?.FieldType ?? property!.PropertyType;
            DisplayName = string.IsNullOrWhiteSpace(attribute.DisplayName) ? Nicify(Name) : attribute.DisplayName!;
        }

        /// <summary>Reflected member.</summary>
        public MemberInfo Member { get; }

        /// <summary>Member name used as the serialization key.</summary>
        public string Name { get; }

        /// <summary>Label shown in the editor.</summary>
        public string DisplayName { get; }

        /// <summary>CLR type of the member.</summary>
        public Type ValueType { get; }

        /// <summary>Inclusive lower bound for numeric values, if specified.</summary>
        public double? Min => double.IsNaN(attribute.Min) ? null : attribute.Min;

        /// <summary>Inclusive upper bound for numeric values, if specified.</summary>
        public double? Max => double.IsNaN(attribute.Max) ? null : attribute.Max;

        /// <summary>Editor increment for numeric values, or null to infer one.</summary>
        public double? Step => double.IsNaN(attribute.Step) ? null : attribute.Step;

        /// <summary>Optional editor tooltip.</summary>
        public string? Tooltip => attribute.Tooltip;

        /// <summary>True when the parameter should not be shown in the inspector.</summary>
        public bool HideInInspector => attribute.HideInInspector;

        /// <summary>True when the member can be written.</summary>
        public bool CanWrite => fieldInfo is not null || (property is not null && property.CanWrite);

        /// <summary>True when the value is a primitive numeric type.</summary>
        public bool IsNumeric => ValueType == typeof(float) || ValueType == typeof(double) ||
                                 ValueType == typeof(int) || ValueType == typeof(long) ||
                                 ValueType == typeof(short) || ValueType == typeof(byte) ||
                                 ValueType == typeof(uint) || ValueType == typeof(ulong);

        /// <summary>Reads the member value from a node.</summary>
        /// <param name="node">Node instance.</param>
        public object? GetValue(Node node) => fieldInfo?.GetValue(node) ?? property?.GetValue(node);

        /// <summary>Writes the member value on a node.</summary>
        /// <param name="node">Node instance.</param>
        /// <param name="value">New value.</param>
        public void SetValue(Node node, object? value)
        {
            if (fieldInfo is not null)
            {
                fieldInfo.SetValue(node, CoerceValue(value, fieldInfo.FieldType));
                return;
            }

            if (property?.CanWrite == true)
                property.SetValue(node, CoerceValue(value, property.PropertyType));
        }

        private static object? CoerceValue(object? value, Type targetType)
        {
            if (value is null) return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
            if (targetType.IsInstanceOfType(value)) return value;

            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            try
            {
                if (underlying.IsEnum) return Enum.ToObject(underlying, value);
                if (underlying == typeof(float)) return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
                if (underlying == typeof(double)) return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                if (underlying == typeof(int)) return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                if (underlying == typeof(bool)) return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                return Activator.CreateInstance(targetType);
            }

            return value;
        }

        private static string Nicify(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            System.Text.StringBuilder builder = new(name.Length + 8);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) builder.Append(' ');
                builder.Append(c);
            }
            return builder.ToString();
        }
    }

    /// <summary>
    /// Caches reflected <see cref="NodePropertyAttribute"/> members for node types.
    /// </summary>
    public static class NodeReflection
    {
        private static readonly ConcurrentDictionary<Type, IReadOnlyList<NodePropertyDescriptor>> Cache = new();

        /// <summary>
        /// Returns all serializable / editable properties declared by a node type, in
        /// declaration order (fields before properties).
        /// </summary>
        /// <param name="type">Node type to inspect.</param>
        public static IReadOnlyList<NodePropertyDescriptor> GetProperties(Type type)
            => Cache.GetOrAdd(type, static t => Build(t));

        /// <summary>
        /// Returns all serializable / editable properties of a node instance.
        /// </summary>
        /// <param name="node">Node to inspect.</param>
        public static IReadOnlyList<NodePropertyDescriptor> GetProperties(Node node)
            => GetProperties(node.GetType());

        private static IReadOnlyList<NodePropertyDescriptor> Build(Type type)
        {
            List<(int Token, NodePropertyDescriptor Descriptor)> members = [];

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            for (Type? current = type; current is not null && current != typeof(Node); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(flags))
                {
                    NodePropertyAttribute? attribute = field.GetCustomAttribute<NodePropertyAttribute>(inherit: true);
                    if (attribute is null) continue;
                    members.Add((field.MetadataToken, new NodePropertyDescriptor(field, attribute)));
                }

                foreach (PropertyInfo property in current.GetProperties(flags))
                {
                    if (property.GetIndexParameters().Length > 0 || property.GetMethod is null) continue;
                    NodePropertyAttribute? attribute = property.GetCustomAttribute<NodePropertyAttribute>(inherit: true);
                    if (attribute is null) continue;
                    members.Add((property.MetadataToken, new NodePropertyDescriptor(property, attribute)));
                }
            }

            return members
                .OrderBy(m => m.Token)
                .Select(m => m.Descriptor)
                .ToArray();
        }
    }
}
