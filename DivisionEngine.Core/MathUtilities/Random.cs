//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
namespace DivisionEngine.MathUtilities
{
    /// <summary>
    /// Represents a pseduo random number generator that holds state.
    /// </summary>
    /// <remarks>This class is a placeholder and has not been tested yet</remarks>
    public static class Random
    {
        private static System.Random _rng = new System.Random();

        /// <summary>
        /// Updates the seed of the random number generator.
        /// </summary>
        /// <param name="seed">New seed to use</param>
        public static void Seed(int seed)
        {
            _rng = new System.Random(seed);
        }

        /// <summary>
        /// Gets a random float between 0.0 and 1.0
        /// </summary>
        /// <returns>Next random float for current rng instance</returns>
        public static float NextFloat() => (float)_rng.NextDouble();

        /// <summary>
        /// Gets a random non-negative float from min to max.
        /// </summary>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <returns>Value between min to max</returns>
        public static float NextFloat(float min, float max) => (float)(_rng.NextDouble() * (max - min) + min);

        /// <summary>
        /// Gets a random non-negative integer from min to max-1.
        /// </summary>
        /// <param name="min">Minimum value</param>
        /// <param name="max">Maximum value</param>
        /// <returns>Value between min to max-1</returns>
        public static int NextInt(int min, int max) => _rng.Next(min, max);

        /// <summary>
        /// Gets a random non-negative integer from 0 to max-1.
        /// </summary>
        /// <param name="max">Maximum value</param>
        /// <returns>Value between 0 to max-1</returns>
        public static int NextInt(int max) => _rng.Next(max);

        /// <summary>
        /// Gets a random non-negative integer
        /// </summary>
        /// <returns>Next random integer for current rng instance</returns>
        public static int NextInt() => _rng.Next();

        /// <summary>
        /// Gets a random boolean value
        /// </summary>
        /// <returns>Next random boolean for current rng instance</returns>
        /// <remarks>50% chance of true or false, uses "NextInt(0, 2) == 0" internally</remarks>
        public static bool NextBool() => _rng.Next(0, 2) == 0;

        /// <summary>
        /// Random generation methods for vectors.
        /// </summary>
        public static class Vector
        {
            public static float2 NextFloat2(float min = 0.0f, float max = 1.0f) => new float2(NextFloat(min, max), NextFloat(min, max));
            public static float3 NextFloat3(float min = 0.0f, float max = 1.0f) => new float3(NextFloat(min, max), NextFloat(min, max), NextFloat(min, max));
            public static float4 NextFloat4(float min = 0.0f, float max = 1.0f) => new float4(NextFloat(min, max), NextFloat(min, max), NextFloat(min, max), NextFloat(min, max));
        }

        /// <summary>
        /// Random generation methods for quaternions.
        /// </summary>
        public static class Quaternion
        {
            /*public static float4 NextQuaternion(float min = 0.0f, float max = 1.0f)
            {
                // Generate a random unit quaternion using the method by Ken Shoemake
                float u1 = NextFloat(min, max);
                float u2 = NextFloat(min, max);
                float u3 = NextFloat(min, max);
                float sqrt1MinusU1 = MathF.Sqrt(1 - u1);
                float sqrtU1 = MathF.Sqrt(u1);
                float theta1 = 2 * MathF.PI * u2;
                float theta2 = 2 * MathF.PI * u3;
                float w = sqrt1MinusU1 * MathF.Sin(theta1);
                float x = sqrt1MinusU1 * MathF.Cos(theta1);
                float y = sqrtU1 * MathF.Sin(theta2);
                float z = sqrtU1 * MathF.Cos(theta2);
                return new float4(x, y, z, w);
            }*/
        }
    }
}
