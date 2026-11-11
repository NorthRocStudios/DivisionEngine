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
    /// Extension methods for quaternion vector operations on float4 objects.
    /// </summary>
    public static class Quaternion
    {
        /// <summary>
        /// Identity quaternion => float4(0, 0, 0, 1).
        /// </summary>
        public static readonly float4 Identity = new float4(0f, 0f, 0f, 1f);

        /// <summary>
        /// Normalizes a quaternion vector.
        /// </summary>
        /// <param name="q">Quaternion to normalize</param>
        /// <returns>Normalized quaternion</returns>
        public static float4 Normalize(this float4 q)
        {
            float length = Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (length < 0.0001f) return Identity;
            return new float4(q.x / length, q.y / length, q.z / length, q.w / length);
        }

        /// <summary>
        /// Create a quaternion rotation from an axis and an angle (in radians).
        /// </summary>
        /// <param name="axis">Axis to rotate around</param>
        /// <param name="angle">Angle in radias</param>
        /// <returns>Quaternion rotation around axis</returns>
        public static float4 CreateFromAxisAngle(float3 axis, float angle) =>
            System.Numerics.Quaternion.CreateFromAxisAngle(axis.ToVector3(), angle).ToFloat4();

        /// <summary>
        /// Creates a quaternion rotation from a float4x4 rotation matrix.
        /// </summary>
        /// <param name="matrix">Rotation matrix</param>
        /// <returns>Quaternion rotation from matrix</returns>
        public static float4 CreateFromRotationMatrix(float4x4 matrix) =>
            System.Numerics.Quaternion.CreateFromRotationMatrix(matrix.ToMatrix4x4()).ToFloat4();

        /// <summary>
        /// Creates a quaternion rotation from yaw, pitch, and roll values.
        /// </summary>
        /// <param name="yaw">Yaw value</param>
        /// <param name="pitch">Pitch value</param>
        /// <param name="roll">Roll value</param>
        /// <returns>Quaternion from yaw, pitch, roll</returns>
        public static float4 CreateFromYawPitchRoll(float yaw, float pitch, float roll) =>
            System.Numerics.Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll).ToFloat4();

        /// <summary>
        /// Inverts this quaternion values.
        /// </summary>
        /// <param name="quaternion">Quaternion to invert</param>
        /// <returns>Inverted quaternion</returns>
        public static float4 InvertQuaternion(this float4 quaternion) =>
            new float4(-quaternion.x, -quaternion.y, -quaternion.z, quaternion.w);

        /// <summary>
        /// Linear interpolation between two quaternions by amount t.
        /// </summary>
        /// <param name="a">Interpolation origin</param>
        /// <param name="b">Interpolation target</param>
        /// <param name="t">Amount to interpolate</param>
        /// <returns>Interpolated quaternion rotation</returns>
        public static float4 Lerp(float4 a, float4 b, float t) =>
            System.Numerics.Quaternion.Lerp(a.ToQuaternion(), b.ToQuaternion(), t).ToFloat4();

        /// <summary>
        /// Adds two quaternions together.
        /// </summary>
        /// <param name="a">Quaternion a</param>
        /// <param name="b">Quaternion b</param>
        /// <returns>Addition of quaterions</returns>
        public static float4 Add(float4 a, float4 b) => (a.ToQuaternion() + b.ToQuaternion()).ToFloat4();

        /// <summary>
        /// Subtracts two quaternions together.
        /// </summary>
        /// <param name="a">Quaternion a</param>
        /// <param name="b">Quaternion b</param>
        /// <returns>Subtraction of quaterions</returns>
        public static float4 Subtract(float4 a, float4 b) => (a.ToQuaternion() - b.ToQuaternion()).ToFloat4();

        /// <summary>
        /// Multiplies two quaternions together.
        /// </summary>
        /// <param name="a">Quaternion a</param>
        /// <param name="b">Quaternion b</param>
        /// <returns>Product of quaterions</returns>
        public static float4 Multiply(float4 a, float4 b) // System.Numerics.Quaternion.* is broken, returns nans
        {
            // Formula: q1 * q2 = (w1*w2 - dot(v1,v2), w1*v2 + w2*v1 + cross(v1,v2))

            float3 v1 = new float3(a.x, a.y, a.z);
            float3 v2 = new float3(b.x, b.y, b.z);
            float w1 = a.w;
            float w2 = b.w;

            float3 crossVec = v1.Cross(v2);
            float dotVec = v1.Dot(v2);

            float3 resultVec = v2.Multiply(w1).Add(v1.Multiply(w2)).Add(crossVec);
            float resultW = w1 * w2 - dotVec;

            return new float4(resultVec.x, resultVec.y, resultVec.z, resultW);
        }

        /// <summary>
        /// Divides two quaternions together.
        /// </summary>
        /// <param name="numerator">Quaternion a</param>
        /// <param name="denominator">Quaternion b</param>
        /// <returns>Division of quaterions</returns>
        public static float4 Divide(float4 numerator, float4 denominator) =>
            (numerator.ToQuaternion() / denominator.ToQuaternion()).ToFloat4();

        /// <summary>
        /// Converts a float4 quaternion to a System.Numerics quaternion.
        /// </summary>
        /// <param name="quaternion">Float4 vector to convert</param>
        /// <returns>System quaternion object</returns>
        public static System.Numerics.Quaternion ToQuaternion(this float4 quaternion) =>
            new System.Numerics.Quaternion(quaternion.x, quaternion.y, quaternion.z, quaternion.w);

        /// <summary>
        /// Converts a system quaternion to a float4 quaternion.
        /// </summary>
        /// <param name="quaternion">System quaternion to convert</param>
        /// <returns>Float4 quaternion vector converted</returns>
        public static float4 ToFloat4(this System.Numerics.Quaternion quaternion) =>
            new float4(quaternion.X, quaternion.Y, quaternion.Z, quaternion.W);

        /// <summary>
        /// Rotates a float3 vector by a quaternion rotation.
        /// </summary>
        /// <param name="quaternion">Quaternion rotation</param>
        /// <param name="vector">Vector to rotate</param>
        /// <returns>Float3 rotated vector</returns>
        public static float3 RotateVector(this float4 quaternion, float3 vector)
        {
            // Standard quaternion rotation: v' = v + 2w(q x v) + 2q x (q x v).
            // This must match ShaderMath.RotateVector exactly - the previous version
            // negated the vector part, which made the CPU compute the conjugate
            // (inverse) rotation while the shader computed the forward one.
            float3 qVec = new float3(quaternion.x, quaternion.y, quaternion.z);
            float3 t = qVec.Cross(vector).Multiply(2.0f);
            return vector.Add(t.Multiply(quaternion.w)).Add(qVec.Cross(t));
        }

        /// <summary>
        /// Rotates a float3 vector by the inverse of a quaternion rotation.
        /// </summary>
        /// <param name="quaternion">Quaternion rotation to invert</param>
        /// <param name="vector">Vector to rotate</param>
        /// <returns>Float3 rotated by the conjugate rotation</returns>
        public static float3 InverseRotateVector(this float4 quaternion, float3 vector) =>
            new float4(-quaternion.x, -quaternion.y, -quaternion.z, quaternion.w).RotateVector(vector);

        /*/// <summary>
        /// Converts a quaternion to Euler angles (roll, pitch, yaw) in radians.
        /// Euler angles are returned in XYZ order (roll = X, pitch = Y, yaw = Z).
        /// </summary>
        /// <param name="q">Quaternion (x, y, z, w)</param>
        /// <returns>Euler angles in radians (x=roll, y=pitch, z=yaw)</returns>
        public static float3 ToEulerAngles(this float4 q)
        {
            // Normalize the quaternion
            float4 qn = q.Normalize();

            float x = qn.X;
            float y = qn.Y;
            float z = qn.Z;
            float w = qn.W;

            // Roll (x-axis rotation)
            float sinr_cosp = 2.0f * (w * x + y * z);
            float cosr_cosp = 1.0f - 2.0f * (x * x + y * y);
            float roll = (float)Math.Atan2(sinr_cosp, cosr_cosp);

            // Pitch (y-axis rotation)
            float sinp = 2.0f * (w * y - z * x);
            float pitch;
            if (Math.Abs(sinp) >= 1.0f)
            {
                // Use 90 degrees if out of range
                pitch = (float)Math.CopySign(PI / 2.0f, sinp);
            }
            else
            {
                pitch = (float)Math.Asin(sinp);
            }

            // Yaw (z-axis rotation)
            float siny_cosp = 2.0f * (w * z + x * y);
            float cosy_cosp = 1.0f - 2.0f * (y * y + z * z);
            float yaw = (float)Math.Atan2(siny_cosp, cosy_cosp);

            return new float3(roll, pitch, yaw);
        }

        /// <summary>
        /// Converts a quaternion to Euler angles in degrees.
        /// </summary>
        public static float3 ToEulerAnglesDegrees(this float4 q)
        {
            float3 radians = q.ToEulerAngles();
            return new float3(
                radians.X * (180.0f / Math.PI),
                radians.Y * (180.0f / Math.PI),
                radians.Z * (180.0f / Math.PI)
            );
        }*/
    }
}
