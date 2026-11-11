//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.MathLib;
using System.Numerics;

namespace DivisionEngine.MathUtilities
{
    /// <summary>
    /// Handles common matrix operations and provides predefined matrices.
    /// </summary>
    public static class Matrix
    {
        /// <summary>
        /// Represents a 2x2 matrix with all elements set to zero.
        /// </summary>
        public static float2x2 Zero2x2 => new float2x2(
            0, 0,
            0, 0
        );

        /// <summary>
        /// Represents a 3x3 matrix with all elements set to zero.
        /// </summary>
        public static float3x3 Zero3x3 => new float3x3(
            0, 0, 0,
            0, 0, 0,
            0, 0, 0
        );

        /// <summary>
        /// Represents a 4x4 matrix with all elements set to zero.
        /// </summary>
        public static float4x4 Zero4x4 => new float4x4(
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0,
            0, 0, 0, 0
        );

        /// <summary>
        /// Represents a 2x2 identity matrix.
        /// </summary>
        public static float2x2 Identity2x2 => new float2x2(
            1, 0,
            0, 1
        );

        /// <summary>
        /// Represents a 3x3 identity matrix.
        /// </summary>
        public static float3x3 Identity3x3 => new float3x3(
            1, 0, 0,
            0, 1, 0,
            0, 0, 1
        );

        /// <summary>
        /// Represents a 4x4 identity matrix.
        /// </summary>
        public static float4x4 Identity4x4 => new float4x4(
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        );

        /// <summary>
        /// Calculates the determinant of a 2x2 matrix.
        /// </summary>
        /// <param name="matrix">Matrix to calculate the determinant of</param>
        /// <returns>Determinant of <param name="matrix"></returns>
        public static float Determinant(this float2x2 matrix) => matrix[0,0] * matrix[1,1] - matrix[0,1] * matrix[1,0];

        /// <summary>
        /// Calculates the determinant of a 3x3 matrix.
        /// </summary>
        /// <param name="matrix">Matrix to calculate the determinant of</param>
        /// <returns>Determinant of <param name="matrix"></returns>
        public static float Determinant(this float3x3 matrix) =>
            matrix[0,0] * (matrix[1,1] * matrix[2,2] - matrix[1,2] * matrix[2,1]) -
            matrix[0,1] * (matrix[1,0] * matrix[2,2] - matrix[1,2] * matrix[2,0]) +
            matrix[0,2] * (matrix[1,0] * matrix[2,1] - matrix[1,1] * matrix[2,0]);

        /// <summary>
        /// Calculates the determinant of a 4x4 matrix.
        /// </summary>
        /// <param name="matrix">Matrix to calculate the determinant of</param>
        /// <returns>Determinant of <param name="matrix"></returns>
        public static float Determinant(this float4x4 matrix) =>
            matrix[0,0] * (matrix[1,1] * (matrix[2,2] * matrix[3,3] - matrix[2,3] * matrix[3,2]) -
                    matrix[1,2] * (matrix[2,1] * matrix[3,3] - matrix[2,3] * matrix[3,1]) +
                    matrix[1,3] * (matrix[2,1] * matrix[3,2] - matrix[2,2] * matrix[3,1])) -
            matrix[0,1] * (matrix[1,0] * (matrix[2,2] * matrix[3,3] - matrix[2,3] * matrix[3,2]) -
                    matrix[1,2] * (matrix[2,0] * matrix[3,3] - matrix[2,3] * matrix[3,0]) +
                    matrix[1,3] * (matrix[2,0] * matrix[3,2] - matrix[2,2] * matrix[3,0])) +
            matrix[0,2] * (matrix[1,0] * (matrix[2,1] * matrix[3,3] - matrix[2,3] * matrix[3,1]) -
                    matrix[1,1] * (matrix[2,0] * matrix[3,3] - matrix[2,3] * matrix[3,0]) +
                    matrix[1,3] * (matrix[2,0] * matrix[3,1] - matrix[2,1] * matrix[3,0])) -
            matrix[0,3] * (matrix[1,0] * (matrix[2,1] * matrix[3,2] - matrix[2,2] * matrix[3,1]) -
                    matrix[1,1] * (matrix[2,0] * matrix[3,2] - matrix[2,2] * matrix[3,0]) +
                    matrix[1,2] * (matrix[2,0] * matrix[3,1] - matrix[2,1] * matrix[3,0]));

        /// <summary>
        /// Converts a float4x4 matrix to a System.Numerics.Matrix4x4.
        /// </summary>
        /// <param name="matrix">Matrix to convert</param>
        /// <returns>Converted matrix</returns>
        public static Matrix4x4 ToMatrix4x4(this float4x4 matrix) => new Matrix4x4(
            matrix[0,0], matrix[0,1], matrix[0,2], matrix[0,3],
            matrix[1,0], matrix[1,1], matrix[1,2], matrix[1,3],
            matrix[2,0], matrix[2,1], matrix[2,2], matrix[2,3],
            matrix[3,0], matrix[3,1], matrix[3,2], matrix[3,3]
        );

        /// <summary>
        /// Converts a System.Numerics.Matrix4x4 to a float4x4.
        /// </summary>
        /// <param name="matrix">Matrix to convert</param>
        /// <returns>Converted matrix</returns>
        public static float4x4 ToFloat4x4(this Matrix4x4 matrix) => new float4x4(
            matrix[0,0], matrix[0,1], matrix[0,2], matrix[0,3],
            matrix[1,0], matrix[1,1], matrix[1,2], matrix[1,3],
            matrix[2,0], matrix[2,1], matrix[2,2], matrix[2,3],
            matrix[3,0], matrix[3,1], matrix[3,2], matrix[3,3]
        );

        /// <summary>
        /// Computes the inverse of the specified 2x2 matrix.
        /// </summary>
        /// <remarks>The method calculates the inverse of the given matrix using a mathematical approach.
        /// If the determinant of the matrix is zero, indicating that the matrix is singular, the method returns the
        /// identity matrix instead.</remarks>
        /// <param name="matrix">The <see cref="float2x2"/> matrix to invert.</param>
        /// <returns>A new <see cref="float2x2"/> representing the inverse of the input matrix. If the matrix is singular
        /// (non-invertible), the returned matrix will be the identity matrix.</returns>
        public static float2x2 Inverse(this float2x2 matrix)
        {
            if (matrix.determinant() == 0)
                return Identity2x2; // Return identity if not invertible
            return matrix.inverse();
        }

        /// <summary>
        /// Computes the inverse of the specified 4x4 matrix.
        /// </summary>
        /// <remarks>The method calculates the inverse of the given matrix using a mathematical approach.
        /// If the determinant of the matrix is zero, indicating that the matrix is singular, the method returns the
        /// identity matrix instead.</remarks>
        /// <param name="matrix">The <see cref="float4x4"/> matrix to invert.</param>
        /// <returns>A new <see cref="float4x4"/> representing the inverse of the input matrix. If the matrix is singular
        /// (non-invertible), the returned matrix will be the identity matrix.</returns>
        public static float4x4 Inverse(this float4x4 matrix)
        {
            if (matrix.determinant() == 0)
                return Identity4x4;
            return matrix.inverse();
        }

        /// <summary>
        /// Transposes the specified float2x2 matrix.
        /// </summary>
        /// <param name="matrix">Matrix to transpose</param>
        /// <returns>Transposed matrix</returns>
        public static float2x2 Transpose(this float2x2 matrix) => matrix.transpose();

        /// <summary>
        /// Transposes the specified float3x3 matrix.
        /// </summary>
        /// <param name="matrix">Matrix to transpose</param>
        /// <returns>Transposed matrix</returns>
        public static float3x3 Transpose(this float3x3 matrix) => matrix.transpose();

        /// <summary>
        /// Transposes the specified float4x4 matrix.
        /// </summary>
        /// <param name="matrix">Matrix to transpose</param>
        /// <returns>Transposed matrix</returns>
        public static float4x4 Transpose(this float4x4 matrix) => matrix.transpose();

        public static float4 Row0(this float4x4 m) => m[0];
        public static float4 Row1(this float4x4 m) => m[1];
        public static float4 Row2(this float4x4 m) => m[2];
        public static float4 Row3(this float4x4 m) => m[3];

        public static float4 Column0(this float4x4 m) => m.c0;
        public static float4 Column1(this float4x4 m) => m.c1;
        public static float4 Column2(this float4x4 m) => m.c2;
        public static float4 Column3(this float4x4 m) => m.c3;

        /// <summary>
        /// Gets the value of this matrix at r, c (correctly on the CPU).
        /// </summary>
        /// <param name="m">Matrix to search</param>
        /// <param name="r">Row value (0 - 3)</param>
        /// <param name="c">Column value (0 - 3)</param>
        /// <returns>0.0 of 0 < r < 4 or 0 < c < 4, or value of matrix</returns>
        /// <remarks>This is made to work on the CPU, doing float4x4[r][c] will NOT work unless in compute shader</remarks>
        public static float GetVal(this float4x4 m, int r, int c) =>
            r >= 0 && r < 4 && c >= 0 && c < 4 ? m[r, c] : 0f;

        /// <summary>
        /// Sets the value of this matrix at r, c (correctly on the CPU).
        /// </summary>
        /// <param name="m">Matrix to insert into</param>
        /// <param name="r">Row value (0 - 3)</param>
        /// <param name="c">Column value (0 - 3)</param>
        /// <returns>Successful or not</returns>
        /// <remarks>This is made to work on the CPU, doing float4x4[r][c] will NOT work unless in compute shader</remarks>
        public static bool SetVal(this ref float4x4 m, int r, int c, float val)
        {
            if (r < 0 || r >= 4 || c < 0 || c >= 4) return false;

            // DivisionMath's indexer has a setter, and `m` is passed by reference, so this
            // actually updates the caller's matrix (the old by-value copy silently did nothing).
            m[r, c] = val;
            return true;
        }

        /// <summary>
        /// Gets the value of this matrix at r, c (correctly on the CPU).
        /// </summary>
        /// <param name="m">Matrix to search</param>
        /// <param name="r">Row value (0 - 2)</param>
        /// <param name="c">Column value (0 - 2)</param>
        /// <returns>0.0 of 0 < r < 3 or 0 < c < 3, or value of matrix</returns>
        /// <remarks>This is made to work on the CPU, doing float3x3[r][c] will NOT work unless in compute shader</remarks>
        public static float GetVal(this float3x3 m, int r, int c) =>
            r >= 0 && r < 3 && c >= 0 && c < 3 ? m[r, c] : 0f;

        /// <summary>
        /// Sets the value of this matrix at r, c (correctly on the CPU).
        /// </summary>
        /// <param name="m">Matrix to insert into</param>
        /// <param name="r">Row value (0 - 2)</param>
        /// <param name="c">Column value (0 - 2)</param>
        /// <returns>Successful or not</returns>
        /// <remarks>This is made to work on the CPU, doing float3x3[r][c] will NOT work unless in compute shader</remarks>
        public static bool SetVal(this ref float3x3 m, int r, int c, float val)
        {
            if (r < 0 || r >= 3 || c < 0 || c >= 3) return false;

            m[r, c] = val;
            return true;
        }

        /// <summary>
        /// Gets the value of this matrix at r, c (correctly on the CPU).
        /// </summary>
        /// <param name="m">Matrix to search</param>
        /// <param name="r">Row value (0 - 1)</param>
        /// <param name="c">Column value (0 - 1)</param>
        /// <returns>0.0 of 0 < r < 2 or 0 < c < 2, or value of matrix</returns>
        /// <remarks>This is made to work on the CPU, doing float2x2[r][c] will NOT work unless in compute shader</remarks>
        public static float GetVal(this float2x2 m, int r, int c) =>
            r >= 0 && r < 2 && c >= 0 && c < 2 ? m[r, c] : 0f;

        /// <summary>
        /// Sets the value of this matrix at r, c (correctly on the CPU).
        /// </summary>
        /// <param name="m">Matrix to insert into</param>
        /// <param name="r">Row value (0 - 2)</param>
        /// <param name="c">Column value (0 - 2)</param>
        /// <returns>Successful or not</returns>
        /// <remarks>This is made to work on the CPU, doing float2x2[r][c] will NOT work unless in compute shader</remarks>
        public static bool SetVal(this ref float2x2 m, int r, int c, float val)
        {
            if (r < 0 || r >= 2 || c < 0 || c >= 2) return false;

            m[r, c] = val;
            return true;
        }

        /// <summary>
        /// Multiplies two 4x4 matrices.
        /// </summary>
        /// <param name="left">Left matrix to multiply</param>
        /// <param name="right">Right matrix to multiply</param>
        /// <returns>Multiplied matrices</returns>
        public static float4x4 Multiply(float4x4 left, float4x4 right) => math.mul(left, right);

        /// <summary>
        /// Creates a 4x4 matrix from a rotation.
        /// </summary>
        /// <param name="quaternion">Quaternion rotation</param>
        /// <returns>Rotation matrix</returns>
        public static float4x4 CreateMatrix4x4FromQuaternion(this float4 quaternion) =>
            Matrix4x4.CreateFromQuaternion(quaternion.ToQuaternion()).ToFloat4x4();

        /// <summary>
        /// Creates a 4x4 matrix from a translation.
        /// </summary>
        /// <param name="translation">Vector translation</param>
        /// <returns>Translation matrix</returns>
        public static float4x4 CreateMatrix4x4FromTranslation(this float3 translation) =>
            Matrix4x4.CreateTranslation(translation.ToVector3()).ToFloat4x4();

        // Test this method before using it in production!
        // The algorithm is based on the following StackOverflow post:
        // The code is commented out because it currently is experimental and untested.
        // https://stackoverflow.com/questions/1148309/inverting-a-4x4-matrix
        /*public static float4x4 Invert(this float4x4 matrix)
        {
            float[] m = [
                matrix[0,0], matrix[0,1], matrix[0,2], matrix[0,3],
                matrix[1,0], matrix[1,1], matrix[1,2], matrix[1,3],
                matrix[2,0], matrix[2,1], matrix[2,2], matrix[2,3],
                matrix[3,0], matrix[3,1], matrix[3,2], matrix[3,3]
            ];
            double[] inv = [16];
            double det;
            int i;

            inv[0] = m[5] * m[10] * m[15] -
                        m[5] * m[11] * m[14] -
                        m[9] * m[6] * m[15] +
                        m[9] * m[7] * m[14] +
                        m[13] * m[6] * m[11] -
                        m[13] * m[7] * m[10];

            inv[4] = -m[4] * m[10] * m[15] +
                        m[4] * m[11] * m[14] +
                        m[8] * m[6] * m[15] -
                        m[8] * m[7] * m[14] -
                        m[12] * m[6] * m[11] +
                        m[12] * m[7] * m[10];

            inv[8] = m[4] * m[9] * m[15] -
                        m[4] * m[11] * m[13] -
                        m[8] * m[5] * m[15] +
                        m[8] * m[7] * m[13] +
                        m[12] * m[5] * m[11] -
                        m[12] * m[7] * m[9];

            inv[12] = -m[4] * m[9] * m[14] +
                        m[4] * m[10] * m[13] +
                        m[8] * m[5] * m[14] -
                        m[8] * m[6] * m[13] -
                        m[12] * m[5] * m[10] +
                        m[12] * m[6] * m[9];

            inv[1] = -m[1] * m[10] * m[15] +
                        m[1] * m[11] * m[14] +
                        m[9] * m[2] * m[15] -
                        m[9] * m[3] * m[14] -
                        m[13] * m[2] * m[11] +
                        m[13] * m[3] * m[10];

            inv[5] = m[0] * m[10] * m[15] -
                        m[0] * m[11] * m[14] -
                        m[8] * m[2] * m[15] +
                        m[8] * m[3] * m[14] +
                        m[12] * m[2] * m[11] -
                        m[12] * m[3] * m[10];

            inv[9] = -m[0] * m[9] * m[15] +
                        m[0] * m[11] * m[13] +
                        m[8] * m[1] * m[15] -
                        m[8] * m[3] * m[13] -
                        m[12] * m[1] * m[11] +
                        m[12] * m[3] * m[9];

            inv[13] = m[0] * m[9] * m[14] -
                        m[0] * m[10] * m[13] -
                        m[8] * m[1] * m[14] +
                        m[8] * m[2] * m[13] +
                        m[12] * m[1] * m[10] -
                        m[12] * m[2] * m[9];

            inv[2] = m[1] * m[6] * m[15] -
                        m[1] * m[7] * m[14] -
                        m[5] * m[2] * m[15] +
                        m[5] * m[3] * m[14] +
                        m[13] * m[2] * m[7] -
                        m[13] * m[3] * m[6];

            inv[6] = -m[0] * m[6] * m[15] +
                        m[0] * m[7] * m[14] +
                        m[4] * m[2] * m[15] -
                        m[4] * m[3] * m[14] -
                        m[12] * m[2] * m[7] +
                        m[12] * m[3] * m[6];

            inv[10] = m[0] * m[5] * m[15] -
                        m[0] * m[7] * m[13] -
                        m[4] * m[1] * m[15] +
                        m[4] * m[3] * m[13] +
                        m[12] * m[1] * m[7] -
                        m[12] * m[3] * m[5];

            inv[14] = -m[0] * m[5] * m[14] +
                        m[0] * m[6] * m[13] +
                        m[4] * m[1] * m[14] -
                        m[4] * m[2] * m[13] -
                        m[12] * m[1] * m[6] +
                        m[12] * m[2] * m[5];

            inv[3] = -m[1] * m[6] * m[11] +
                        m[1] * m[7] * m[10] +
                        m[5] * m[2] * m[11] -
                        m[5] * m[3] * m[10] -
                        m[9] * m[2] * m[7] +
                        m[9] * m[3] * m[6];

            inv[7] = m[0] * m[6] * m[11] -
                        m[0] * m[7] * m[10] -
                        m[4] * m[2] * m[11] +
                        m[4] * m[3] * m[10] +
                        m[8] * m[2] * m[7] -
                        m[8] * m[3] * m[6];

            inv[11] = -m[0] * m[5] * m[11] +
                        m[0] * m[7] * m[9] +
                        m[4] * m[1] * m[11] -
                        m[4] * m[3] * m[9] -
                        m[8] * m[1] * m[7] +
                        m[8] * m[3] * m[5];

            inv[15] = m[0] * m[5] * m[10] -
                        m[0] * m[6] * m[9] -
                        m[4] * m[1] * m[10] +
                        m[4] * m[2] * m[9] +
                        m[8] * m[1] * m[6] -
                        m[8] * m[2] * m[5];

            det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];

            if (det == 0)
                return Matrix.Zero4x4;

            det = 1.0 / det;

            float[] invOut = [16];
            for (i = 0; i < 16; i++)
                invOut[i] = (float)(inv[i] * det);

            return new float4x4(invOut[0], invOut[1], invOut[2], invOut[3],
                                  invOut[4], invOut[5], invOut[6], invOut[7],
                                  invOut[8], invOut[9], invOut[10], invOut[11],
                                  invOut[12], invOut[13], invOut[14], invOut[15]);
        }*/
    }
}
