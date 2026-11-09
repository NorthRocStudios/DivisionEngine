//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Collections.Concurrent;

namespace DivisionEngine.Audio
{
    /// <summary>
    /// Windowing functions applied before an FFT.
    /// </summary>
    public enum FftWindow
    {
        /// <summary>No window (rectangular).</summary>
        None,
        /// <summary>Hann window, good general purpose choice.</summary>
        Hann,
        /// <summary>Hamming window.</summary>
        Hamming,
    }

    /// <summary>
    /// Radix-2 Cooley-Tukey FFT used for spectrum analysis of playing audio.
    /// Tables are cached per FFT size, so repeated analysis is allocation free.
    /// </summary>
    public static class Fft
    {
        private sealed class Tables(int size)
        {
            public readonly int Size = size;
            public readonly int[] Reversed = BuildBitReversal(size);
            public readonly float[] Cos = BuildCos(size);
            public readonly float[] Sin = BuildSin(size);
            public readonly float[] Window = new float[size];
        }

        private static readonly ConcurrentDictionary<(int Size, FftWindow Window), Tables> cache = [];

        private static int[] BuildBitReversal(int n)
        {
            int[] reversed = new int[n];
            int bits = System.Numerics.BitOperations.Log2((uint)n);
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                    if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                reversed[i] = r;
            }
            return reversed;
        }

        private static float[] BuildCos(int n)
        {
            float[] table = new float[n / 2];
            for (int i = 0; i < table.Length; i++)
                table[i] = (float)Math.Cos(-2d * Math.PI * i / n);
            return table;
        }

        private static float[] BuildSin(int n)
        {
            float[] table = new float[n / 2];
            for (int i = 0; i < table.Length; i++)
                table[i] = (float)Math.Sin(-2d * Math.PI * i / n);
            return table;
        }

        private static Tables GetTables(int size, FftWindow window)
        {
            return cache.GetOrAdd((size, window), key =>
            {
                Tables t = new(key.Size);
                FillWindow(t.Window, key.Window);
                return t;
            });
        }

        private static void FillWindow(float[] window, FftWindow type)
        {
            int n = window.Length;
            for (int i = 0; i < n; i++)
            {
                window[i] = type switch
                {
                    FftWindow.Hann => (float)(0.5 - 0.5 * Math.Cos(2d * Math.PI * i / (n - 1))),
                    FftWindow.Hamming => (float)(0.54 - 0.46 * Math.Cos(2d * Math.PI * i / (n - 1))),
                    _ => 1f,
                };
            }
        }

        /// <summary>
        /// Returns true when <paramref name="size"/> is a usable power of two.
        /// </summary>
        public static bool IsPowerOfTwo(int size) => size >= 2 && (size & (size - 1)) == 0;

        /// <summary>
        /// Computes single-sided magnitude spectrum of a real-valued frame.
        /// </summary>
        /// <param name="frame">Time domain samples (length must be a power of two).</param>
        /// <param name="magnitudes">Receives frame.Length/2 magnitude bins, normalized so a full-scale sine is ~1.</param>
        /// <param name="window">Window function to apply.</param>
        public static void Magnitudes(ReadOnlySpan<float> frame, Span<float> magnitudes, FftWindow window = FftWindow.Hann)
        {
            int n = frame.Length;
            if (!IsPowerOfTwo(n)) throw new ArgumentException("FFT size must be a power of two.", nameof(frame));
            if (magnitudes.Length < n / 2) throw new ArgumentException("Magnitude buffer too small.", nameof(magnitudes));

            Tables t = GetTables(n, window);

            float[] re = new float[n];
            float[] im = new float[n];

            for (int i = 0; i < n; i++)
                re[t.Reversed[i]] = frame[i] * t.Window[i];

            for (int len = 2; len <= n; len <<= 1)
            {
                int half = len >> 1;
                int step = n / len;
                for (int i = 0; i < n; i += len)
                {
                    for (int j = 0; j < half; j++)
                    {
                        int k = j * step;
                        float cos = t.Cos[k];
                        float sin = t.Sin[k];
                        int a = i + j;
                        int b = a + half;
                        float tr = re[b] * cos - im[b] * sin;
                        float ti = re[b] * sin + im[b] * cos;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                    }
                }
            }

            float scale = 2f / n;
            for (int i = 0; i < n / 2; i++)
                magnitudes[i] = MathF.Sqrt(re[i] * re[i] + im[i] * im[i]) * scale;
        }
    }
}
