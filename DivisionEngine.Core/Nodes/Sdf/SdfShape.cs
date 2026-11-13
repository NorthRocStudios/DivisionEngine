//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Nodes.Sdf
{
    /// <summary>
    /// A signed distance function produced by a geometry node. The shape is a pure
    /// function of position, so nodes can be composed (union, subtract, transform)
    /// without any GPU state.
    /// </summary>
    public sealed class SdfShape
    {
        private readonly Func<float3, float> distance;

        /// <summary>
        /// Creates a shape from a distance function.
        /// </summary>
        /// <param name="distance">Signed distance function; negative inside the shape.</param>
        /// <param name="color">Suggested RGBA colour used by previews.</param>
        /// <param name="description">Human readable description for debugging and previews.</param>
        public SdfShape(Func<float3, float> distance, float4 color, string description = "SDF")
        {
            this.distance = distance ?? throw new ArgumentNullException(nameof(distance));
            Color = color;
            Description = description;
        }

        /// <summary>Suggested RGBA colour for previews.</summary>
        public float4 Color { get; }

        /// <summary>Human readable description of the shape.</summary>
        public string Description { get; }

        /// <summary>Evaluates the signed distance at a point.</summary>
        /// <param name="point">Position in shape space.</param>
        /// <returns>Signed distance; negative inside the shape.</returns>
        public float Evaluate(float3 point) => distance(point);

        /// <summary>
        /// Approximates the surface normal by central differences of the distance field.
        /// </summary>
        /// <param name="point">Position to sample.</param>
        /// <param name="epsilon">Offset used for each finite difference.</param>
        public float3 Normal(float3 point, float epsilon = 0.001f)
        {
            float dx = Evaluate(new float3(point.x + epsilon, point.y, point.z)) -
                       Evaluate(new float3(point.x - epsilon, point.y, point.z));
            float dy = Evaluate(new float3(point.x, point.y + epsilon, point.z)) -
                       Evaluate(new float3(point.x, point.y - epsilon, point.z));
            float dz = Evaluate(new float3(point.x, point.y, point.z + epsilon)) -
                       Evaluate(new float3(point.x, point.y, point.z - epsilon));
            float length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            return length > 1e-8f ? new float3(dx / length, dy / length, dz / length) : new float3(0, 1, 0);
        }
    }
}
