//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.MathUtilities
{
    /// <summary>
    /// Mathematical utilities for Division Engine.
    /// </summary>
    public static class Math
    {
        // Constants

        /// <summary>
        /// Represents the mathematical constant π (pi), the ratio of a circle's circumference to its diameter.
        /// </summary>
        /// <remarks>The value of this constant is approximately 3.14159. It is provided as a
        /// single-precision floating-point number.</remarks>
        public const float PI = (float)System.Math.PI;
        /// <summary>
        /// Represents the mathematical constant e (Euler's number), approximately equal to 2.718.
        /// </summary>
        /// <remarks>This constant is defined as a single-precision floating-point value. It is commonly
        /// used as the base of natural logarithms and in exponential calculations.</remarks>
        public const float E = (float)System.Math.E;
        /// <summary>
        /// Represents the mathematical constant τ (Tau), which is equal to 2π.
        /// </summary>
        /// <remarks>Tau is commonly used in mathematics and physics as an alternative to 2π, particularly
        /// in contexts involving circles and periodic functions.</remarks>
        public const float Tau = 2 * PI; // Tau is 2*Pi
        /// <summary>
        /// Represents the factor used to convert degrees to radians.
        /// </summary>
        /// <remarks>Multiply an angle in degrees by this constant to convert it to radians. For example,
        /// to convert 90 degrees to radians, calculate <c>90 * Deg2Rad</c>.</remarks>
        public const float Deg2Rad = PI / 180f;
        /// <summary>
        /// Represents the conversion factor from radians to degrees.
        /// </summary>
        /// <remarks>Multiply a value in radians by this constant to convert it to degrees. The value is
        /// equivalent to 180 divided by π.</remarks>
        public const float Rad2Deg = 180f / PI;
        /// <summary>
        /// Represents positive infinity for floating-point numbers.
        /// </summary>
        public const float Infinity = float.PositiveInfinity;
        /// <summary>
        /// Represents negative infinity for floating-point numbers.
        /// </summary>
        public const float NegativeInfinity = float.NegativeInfinity;

        // Basic Math Functions

        /// <summary>
        /// Returns the absolute value of a number.
        /// </summary>
        /// <param name="value">The value to find the absolute of</param>
        /// <returns>The absolute of <paramref name="value"/></returns>
        public static float Abs(float value) => System.Math.Abs(value);
        public static double Abs(double value) => System.Math.Abs(value);
        public static int Abs(int value) => System.Math.Abs(value);
        /// <summary>
        /// Computes the sine of the specified <paramref name="angle"/>.
        /// </summary>
        /// <param name="angle">The angle to compute the sine of</param>
        /// <returns>The sine of <paramref name="angle"/></returns>
        public static float Sin(float angle) => (float)System.Math.Sin(angle);
        /// <summary>
        /// Computes the cosine of the specified <paramref name="angle"/>.
        /// </summary>
        /// <param name="angle">The angle to compute the cosine of</param>
        /// <returns>The cosine of <paramref name="angle"/></returns>
        public static float Cos(float angle) => (float)System.Math.Cos(angle);
        /// <summary>
        /// Computes the tangent of the specified <paramref name="angle"/>.
        /// </summary>
        /// <param name="angle">The angle to compute the tangent of</param>
        /// <returns>The tangent of <paramref name="angle"/></returns>
        public static float Tan(float angle) => (float)System.Math.Tan(angle);
        /// <summary>
        /// Computes the arc-sine of the specified <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The angle to compute the arc-sine of</param>
        /// <returns>The arc-sine of <paramref name="value"/></returns>
        public static float Asin(float value) => (float)System.Math.Asin(value);
        /// <summary>
        /// Computes the arc-cosine of the specified <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The angle to compute the arc-cosine of</param>
        /// <returns>The arc-cosine of <paramref name="value"/></returns>
        public static float Acos(float value) => (float)System.Math.Acos(value);
        /// <summary>
        /// Computes the arc-tangent of the specified <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The angle to compute the arc-tangent of</param>
        /// <returns>The arc-tangent of <paramref name="value"/></returns>
        public static float Atan(float value) => (float)System.Math.Atan(value);
        public static double Atan(double value) => System.Math.Atan(value);
        /// <summary>
        /// Computes the angle (in radians) whose tangent is the quotient of two specified numbers, y and x.
        /// </summary>
        /// <returns>The angle in radians</returns>
        public static float Atan2(float y, float x) => (float)System.Math.Atan2(y, x);
        public static double Atan2(double y, double x) => System.Math.Atan2(y, x);
        public static float Pow(float x, float y) => (float)System.Math.Pow(x, y);
        public static double Pow(double x, double y) => System.Math.Pow(x, y);
        public static float Exp(float x) => (float)System.Math.Exp(x);
        public static double Exp(double x) => System.Math.Exp(x);
        public static float Log(float x) => (float)System.Math.Log(x);
        public static double Log(double x) => System.Math.Log(x);
        public static float Log2(float x) => (float)System.Math.Log2(x);
        public static double Log2(double x) => System.Math.Log2(x);
        public static float Log(float x, float newBase) => (float)System.Math.Log(x, newBase);
        public static double Log(double x, double newBase) => System.Math.Log(x, newBase);
        public static float Log10(float x) => (float)System.Math.Log10(x);
        /// <summary>
        /// Floors a value to a whole number.
        /// </summary>
        /// <param name="x">Value to floor</param>
        /// <returns>Floored value</returns>
        public static float Floor(float x) => (float)System.Math.Floor(x);
        public static double Floor(double x) => System.Math.Floor(x);
        /// <summary>
        /// Floors a value to an integer.
        /// </summary>
        /// <param name="x">Value to floor</param>
        /// <returns>Floored integer value</returns>
        public static int FloorToInt(float x) => (int)System.Math.Floor(x);
        public static int FloorToInt(double x) => (int)System.Math.Floor(x);
        public static float Ceiling(float x) => (float)System.Math.Ceiling(x);
        public static double Ceiling(double x) => System.Math.Ceiling(x);
        public static int CeilingToInt(float x) => (int)System.Math.Ceiling(x);
        public static int CeilingToInt(double x) => (int)System.Math.Ceiling(x);
        public static float Round(float x) => (float)System.Math.Round(x);
        public static double Round(double x) => System.Math.Round(x);
        public static int RoundToInt(float x) => (int)System.Math.Round(x);
        public static int RoundToInt(double x) => (int)System.Math.Round(x);
        public static int Sign(float x) => System.Math.Sign(x);
        public static int Sign(int x) => System.Math.Sign(x);
        /// <summary>
        /// Finds the max between two floating point values.
        /// </summary>
        /// <param name="a">Float a</param>
        /// <param name="b">Float b</param>
        /// <returns>Max between a and b</returns>
        public static float Max(float a, float b) => (a > b) ? a : b;
        /// <summary>
        /// Finds the max between two integers.
        /// </summary>
        /// <param name="a">Integer a</param>
        /// <param name="b">Integer b</param>
        /// <returns>Max between a and b</returns>
        public static int Max(int a, int b) => (a > b) ? a : b;
        /// <summary>
        /// Finds the min between two floating point values.
        /// </summary>
        /// <param name="a">Float a</param>
        /// <param name="b">Float b</param>
        /// <returns>Min between a and b</returns>
        public static float Min(float a, float b) => (a < b) ? a : b;
        /// <summary>
        /// Finds the min between two values.
        /// </summary>
        /// <param name="a">Value a</param>
        /// <param name="b">Value b</param>
        /// <returns>Min between a and b</returns>
        public static double Min(double a, double b) => (a < b) ? a : b;
        /// <summary>
        /// Finds the min between two integers.
        /// </summary>
        /// <param name="a">Integer a</param>
        /// <param name="b">Integer b</param>
        /// <returns>Min between a and b</returns>
        public static int Min(int a, int b) => (a < b) ? a : b;

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static int Lerp(int a, int b, float t) => (int)(a + (b - a) * Clamp01(t));
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static int LerpUnclamped(int a, int b, float t) => (int)(a + (b - a) * t);
        /// <summary>
        /// Finds the square root of a value.
        /// </summary>
        /// <param name="x">Value to root</param>
        /// <returns>Square root of value</returns>
        public static float Sqrt(float x) => (float)System.Math.Sqrt(x);
        public static double Sqrt(double x) => System.Math.Sqrt(x);

        /// <summary>
        /// Restricts a value to be within a specified range.
        /// </summary>
        /// <param name="value">The value to clamp.</param>
        /// <param name="min">The inclusive lower bound of the range.</param>
        /// <param name="max">The inclusive upper bound of the range.</param>
        /// <returns>The value clamped to the range defined by <paramref name="min"/> and <paramref name="max"/>. If <paramref
        /// name="value"/> is less than <paramref name="min"/>, <paramref name="min"/> is returned. If <paramref
        /// name="value"/> is greater than <paramref name="max"/>, <paramref name="max"/> is returned. Otherwise,
        /// <paramref name="value"/> is returned.</returns>
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        
        /// <summary>
        /// Restricts a value to be within a specified range.
        /// </summary>
        /// <param name="value">The value to clamp.</param>
        /// <param name="min">The inclusive lower bound of the range.</param>
        /// <param name="max">The inclusive upper bound of the range.</param>
        /// <returns>The value clamped to the range defined by <paramref name="min"/> and <paramref name="max"/>. If <paramref
        /// name="value"/> is less than <paramref name="min"/>, <paramref name="min"/> is returned. If <paramref
        /// name="value"/> is greater than <paramref name="max"/>, <paramref name="max"/> is returned. Otherwise,
        /// <paramref name="value"/> is returned.</returns>
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// Clamps a value to the range [0, 1].
        /// </summary>
        /// <param name="value">The value to clamp.</param>
        /// <returns>The clamped value, which will be 0 if <paramref name="value"/> is less than 0, 1 if <paramref
        /// name="value"/> is greater than 1, or the original value if it is within the range [0, 1].</returns>
        public static float Clamp01(float value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }

        /// <summary>
        /// Clamps a value to the range [0, 1].
        /// </summary>
        /// <param name="value">The value to clamp.</param>
        /// <returns>The clamped value, which will be 0 if <paramref name="value"/> is less than 0, 1 if <paramref
        /// name="value"/> is greater than 1, or the original value if it is within the range [0, 1].</returns>
        public static int Clamp01(int value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }

        public static float Remap(float value, float from1, float to1, float from2, float to2)
        {
            return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
        }

        public static int Remap(int value, int from1, int to1, int from2, int to2)
        {
            return (int)((value - from1) / (float)(to1 - from1) * (to2 - from2) + from2);
        }

        public static float WrapAngle(float angle)
        {
            angle = angle % 360;
            if (angle < 0) angle += 360;
            return angle;
        }

        public static float WrapAngleRadians(float angle)
        {
            angle = angle % Tau;
            if (angle < 0) angle += Tau;
            return angle;
        }

        /// <summary>
        /// Converts a quaternion to Euler radian angles.
        /// </summary>
        /// <param name="q">Quaternion to convert</param>
        /// <returns>Euler radian angle result</returns>
        public static float3 QuaternionToEuler(this float4 q)
        {
            // Roll (x-axis rotation)
            float sinr_cosp = 2 * (q.w * q.x + q.y * q.z);
            float cosr_cosp = 1 - 2 * (q.x * q.x + q.y * q.y);
            float roll = Atan2(sinr_cosp, cosr_cosp);

            // Pitch (y-axis rotation)
            float sinp = 2 * (q.w * q.y - q.z * q.x);
            float pitch;
            if (Abs(sinp) >= 1)
                pitch = PI / 2 * Sign(sinp); // use 90 degrees if out of range
            else
                pitch = Asin(sinp);

            // Yaw (z-axis rotation)
            float siny_cosp = 2 * (q.w * q.z + q.x * q.y);
            float cosy_cosp = 1 - 2 * (q.y * q.y + q.z * q.z);
            float yaw = Atan2(siny_cosp, cosy_cosp);
            return new float3(roll, pitch, yaw);
        }

        /// <summary>
        /// Converts Euler angles (in radians) to a quaternion.
        /// </summary>
        /// <param name="euler">Euler radians</param>
        /// <returns>Quaternion conversion value</returns>
        public static float4 EulerToQuaternion(this float3 euler)
        {
            // Abbreviations for the various angular functions
            double cy = Cos(euler.z * 0.5f);
            double sy = Sin(euler.z * 0.5f);
            double cr = Cos(euler.x * 0.5f);
            double sr = Sin(euler.x * 0.5f);
            double cp = Cos(euler.y * 0.5f);
            double sp = Sin(euler.y * 0.5f);
            float w = (float)(cy * cr * cp + sy * sr * sp);
            float x = (float)(cy * sr * cp - sy * cr * sp);
            float y = (float)(cy * cr * sp + sy * sr * cp);
            float z = (float)(sy * cr * cp - cy * sr * sp);
            return new float4(x, y, z, w);
        }

        /// <summary>
        /// Copies the sign from one number onto the value of another.
        /// </summary>
        /// <param name="magnitude">Value to sign</param>
        /// <param name="sign">Sign to copy to value</param>
        /// <returns>Signed value</returns>
        public static float CopySign(float magnitude, float sign) => Abs(magnitude) * Sign(sign);
    }
}
