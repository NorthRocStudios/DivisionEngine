//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Nodes.Sdf;
using System.Numerics;

namespace DivisionEngine.Nodes
{
    /// <summary>
    /// Broad value categories that flow through node connections. The kind drives port
    /// colours in the editor and which connections are legal.
    /// </summary>
    public enum NodeValueKind
    {
        /// <summary>Accepts a value of any kind.</summary>
        Any,
        /// <summary>Single precision floating point value.</summary>
        Float,
        /// <summary>Signed integer value.</summary>
        Int,
        /// <summary>Boolean value.</summary>
        Bool,
        /// <summary>Text value.</summary>
        String,
        /// <summary>Two component vector.</summary>
        Vector2,
        /// <summary>Three component vector.</summary>
        Vector3,
        /// <summary>Four component vector.</summary>
        Vector4,
        /// <summary>RGBA colour (stored as a float4).</summary>
        Color,
        /// <summary>Texture reference.</summary>
        Texture,
        /// <summary>Audio clip, buffer or generated signal.</summary>
        Audio,
        /// <summary>Signed distance field shape.</summary>
        Sdf,
        /// <summary>Material description.</summary>
        Material,
        /// <summary>Arbitrary object exposed by a custom node.</summary>
        Object,
    }

    /// <summary>
    /// Describes the value carried by a node port. The <see cref="ClrType"/> is optional
    /// and only used for display and for object ports that need runtime type checks.
    /// </summary>
    public readonly record struct NodeDataType
    {
        /// <summary>
        /// Creates a new port data type.
        /// </summary>
        /// <param name="kind">Broad value category.</param>
        /// <param name="clrType">Optional CLR type carried by the port.</param>
        public NodeDataType(NodeValueKind kind, Type? clrType = null)
        {
            Kind = kind;
            ClrType = clrType;
        }

        /// <summary>Broad value category.</summary>
        public NodeValueKind Kind { get; }

        /// <summary>CLR type carried by the port, when known.</summary>
        public Type? ClrType { get; }

        /// <summary>Friendly name shown in the editor.</summary>
        public string DisplayName => Kind switch
        {
            NodeValueKind.Vector2 => "Vector2",
            NodeValueKind.Vector3 => "Vector3",
            NodeValueKind.Vector4 => "Vector4",
            NodeValueKind.Sdf => "SDF",
            NodeValueKind.Object => ClrType?.Name ?? "Object",
            _ => Kind.ToString(),
        };

        /// <summary>Convenience instance for mixed / untyped ports.</summary>
        public static NodeDataType Any => new(NodeValueKind.Any);

        /// <summary>Convenience instance for float ports.</summary>
        public static NodeDataType Float => new(NodeValueKind.Float, typeof(float));

        /// <summary>Convenience instance for integer ports.</summary>
        public static NodeDataType Int => new(NodeValueKind.Int, typeof(int));

        /// <summary>Convenience instance for boolean ports.</summary>
        public static NodeDataType Bool => new(NodeValueKind.Bool, typeof(bool));

        /// <summary>Convenience instance for string ports.</summary>
        public static NodeDataType String => new(NodeValueKind.String, typeof(string));

        /// <summary>Convenience instance for vector2 ports.</summary>
        public static NodeDataType Vector2 => new(NodeValueKind.Vector2, typeof(float2));

        /// <summary>Convenience instance for vector3 ports.</summary>
        public static NodeDataType Vector3 => new(NodeValueKind.Vector3, typeof(float3));

        /// <summary>Convenience instance for vector4 ports.</summary>
        public static NodeDataType Vector4 => new(NodeValueKind.Vector4, typeof(float4));

        /// <summary>Convenience instance for colour ports.</summary>
        public static NodeDataType Color => new(NodeValueKind.Color, typeof(float4));

        /// <summary>Convenience instance for signed distance field shape ports.</summary>
        public static NodeDataType Sdf => new(NodeValueKind.Sdf, typeof(SdfShape));

        /// <summary>Convenience instance for audio ports.</summary>
        public static NodeDataType Audio => new(NodeValueKind.Audio);

        /// <summary>Convenience instance for material ports.</summary>
        public static NodeDataType Material => new(NodeValueKind.Material);

        /// <summary>Convenience instance for texture ports.</summary>
        public static NodeDataType Texture => new(NodeValueKind.Texture);

        /// <summary>
        /// Infers a port data type from a CLR type.
        /// </summary>
        /// <param name="type">CLR type to inspect.</param>
        /// <returns>The closest matching port data type.</returns>
        public static NodeDataType FromType(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);

            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return new(NodeValueKind.Float, type);
            if (type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return new(NodeValueKind.Int, type);
            if (type == typeof(bool)) return new(NodeValueKind.Bool, type);
            if (type == typeof(string)) return new(NodeValueKind.String, type);
            if (type == typeof(float2) || type == typeof(Vector2)) return Vector2;
            if (type == typeof(float3) || type == typeof(Vector3)) return Vector3;
            if (type == typeof(float4) || type == typeof(Vector4)) return Vector4;
            if (type == typeof(SdfShape)) return Sdf;
            if (typeof(NodeAudioValue).IsAssignableFrom(type)) return Audio;
            if (typeof(NodeMaterialValue).IsAssignableFrom(type)) return Material;
            return new NodeDataType(NodeValueKind.Object, type);
        }

        /// <summary>
        /// Returns true when an output of this type can feed an input of
        /// <paramref name="inputType"/>.
        /// </summary>
        /// <param name="inputType">Type of the receiving input port.</param>
        public bool IsCompatibleWith(NodeDataType inputType)
        {
            if (Kind == NodeValueKind.Any || inputType.Kind == NodeValueKind.Any) return true;

            if (Kind == inputType.Kind)
            {
                // Object ports additionally require the CLR types to be assignable.
                if (Kind == NodeValueKind.Object && ClrType is not null && inputType.ClrType is not null)
                    return inputType.ClrType.IsAssignableFrom(ClrType);
                return true;
            }

            // Numeric widening, and colour / vector interchange.
            if (Kind == NodeValueKind.Int && inputType.Kind == NodeValueKind.Float) return true;
            if (Kind == NodeValueKind.Vector3 && inputType.Kind == NodeValueKind.Color) return true;
            if (Kind == NodeValueKind.Vector4 && inputType.Kind == NodeValueKind.Color) return true;
            if (Kind == NodeValueKind.Color && inputType.Kind == NodeValueKind.Vector3) return true;
            if (Kind == NodeValueKind.Color && inputType.Kind == NodeValueKind.Vector4) return true;
            return false;
        }

        /// <inheritdoc />
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Base value for audio flowing through a node graph. Concrete editors can derive
    /// from this to carry generated clips, buffers or parameter streams.
    /// </summary>
    public abstract class NodeAudioValue
    {
        /// <summary>Sample rate of the signal in hertz.</summary>
        public abstract int SampleRate { get; }

        /// <summary>Samples the signal at a time in seconds.</summary>
        /// <param name="seconds">Time to sample, in seconds.</param>
        public abstract float Sample(float seconds);
    }

    /// <summary>
    /// Base value for material descriptions flowing through a node graph.
    /// </summary>
    public abstract class NodeMaterialValue
    {
        /// <summary>Default RGBA colour of the material.</summary>
        public abstract float4 Albedo { get; }
    }
}
