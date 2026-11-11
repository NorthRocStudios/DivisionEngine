//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Projects.Assets;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace DivisionEngine.Serialization
{
    /// <summary>
    /// Handles serialization for Division Engine, fills in the gaps missing from System.Text.Json.
    /// </summary>
    public static class Serialize
    {
        /// <summary>
        /// Default serialization settings.
        /// </summary>
        private static JsonSerializerOptions DefaultSerializationSettings => new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        /// <summary>
        /// Gets the default json serialization of an object.
        /// </summary>
        /// <param name="obj">Object to serialize</param>
        /// <returns>Serialized json representation of object</returns>
        public static string Default(object? obj) => JsonSerializer.Serialize(obj, DefaultSerializationSettings);

        /// <summary>
        /// Serializes a component (ensures correct formatting of fields).
        /// </summary>
        /// <param name="component">Component to serialize</param>
        /// <returns>Serialized dictionary where keys are field names and values are serialized field values</returns>
        public static Dictionary<string, string> Component(IComponent component)
        {
            FieldInfo[] fields = component.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Dictionary<string, string> serialized = [];

            foreach (FieldInfo field in fields)
            {
                // Skip certain field modifiers
                if (field.IsLiteral && !field.IsInitOnly) continue;
                if (field.IsStatic) continue;
                if (field.IsInitOnly) continue;
                if (field.GetCustomAttribute<NonSerializedAttribute>() != null) continue;
                if (field.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() != null) continue;

                Type fieldType = field.FieldType;
                object? fieldVal = field.GetValue(component);
                if (fieldVal == null)
                    serialized.Add(field.Name, "null");
                else // Adaptive serialization for some special types
                {
                    string serializedField = fieldVal.ToString()!;

                    // Handle custom types
                    if (fieldType == typeof(AssetRef) ||
                        (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(AssetRef<>)))
                    {
                        // Get ID and ExpectedType via reflection
                        PropertyInfo? idProp = fieldType.GetProperty("ID");
                        PropertyInfo? typeProp = fieldType.GetProperty("ExpectedType");

                        string id = idProp != null ?
                            (idProp.GetValue(fieldVal)?.ToString() ?? "") : "";
                        int expectedType = typeProp != null ?
                            (int)(typeProp.GetValue(fieldVal) ?? 0) : 0;

                        // Store as special format: "(AssetRef:{id}:{expectedType})"
                        serializedField = $"(AssetRef:{id}:{expectedType})";
                    }
                    else if (fieldType == typeof(float2))
                    {
                        float2 vec = (float2)fieldVal;
                        // Invariant culture: ',' is the field separator, so a comma-decimal
                        // locale would otherwise corrupt the value on the way in and out.
                        serializedField = FormattableString.Invariant($"({vec.x},{vec.y})");
                    }
                    else if (fieldType == typeof(float3))
                    {
                        float3 vec = (float3)fieldVal;
                        serializedField = FormattableString.Invariant($"({vec.x},{vec.y},{vec.z})");
                    }
                    else if (fieldType == typeof(float4))
                    {
                        float4 vec = (float4)fieldVal;
                        serializedField = FormattableString.Invariant($"({vec.x},{vec.y},{vec.z},{vec.w})");
                    }
                    else if (fieldType == typeof(float4x4))
                    {
                        float4x4 matrix = (float4x4)fieldVal;
                        serializedField = Serialize4x4Matrix(matrix);
                    }

                    serialized.Add(field.Name, serializedField);
                }
            }
            return serialized;
        }

        /// <summary>
        /// Formats a matrix with the invariant culture (see <see cref="Component"/>: the ',' is
        /// the field separator, so the culture must never be allowed to introduce extra commas).
        /// </summary>
        private static string Serialize4x4Matrix(float4x4 matrix)
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            string values = string.Join(",",
                matrix[0,0].ToString(culture), matrix[0,1].ToString(culture), matrix[0,2].ToString(culture), matrix[0,3].ToString(culture),
                matrix[1,0].ToString(culture), matrix[1,1].ToString(culture), matrix[1,2].ToString(culture), matrix[1,3].ToString(culture),
                matrix[2,0].ToString(culture), matrix[2,1].ToString(culture), matrix[2,2].ToString(culture), matrix[2,3].ToString(culture),
                matrix[3,0].ToString(culture), matrix[3,1].ToString(culture), matrix[3,2].ToString(culture), matrix[3,3].ToString(culture)
            );
            return $"({values})";
        }
    }
}
