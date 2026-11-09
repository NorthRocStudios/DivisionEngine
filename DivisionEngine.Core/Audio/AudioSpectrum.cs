//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
namespace DivisionEngine.Audio
{
    /// <summary>
    /// Result of analyzing a chunk of audio. Exposed to gameplay code so effects
    /// (VFX, lighting, UI) can react to the currently playing sound.
    /// </summary>
    public sealed class AudioSpectrum
    {
        /// <summary>
        /// Number of log-spaced frequency bands in <see cref="Bands"/>.
        /// </summary>
        public int BandCount => Bands.Length;

        /// <summary>
        /// Normalized (0-1) energy per log-spaced frequency band.
        /// </summary>
        public float[] Bands { get; }

        /// <summary>
        /// Raw single-sided FFT magnitudes.
        /// </summary>
        public float[] Magnitudes { get; }

        /// <summary>
        /// FFT size used for this analysis.
        /// </summary>
        public int FftSize { get; }

        /// <summary>
        /// Sample rate of the analyzed audio.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Root mean square level of the analyzed window (0-1).
        /// </summary>
        public float Rms { get; }

        /// <summary>
        /// Peak absolute sample in the analyzed window (0-1).
        /// </summary>
        public float Peak { get; }

        /// <summary>
        /// Loudness of the window in decibels (<= 0).
        /// </summary>
        public float LoudnessDb { get; }

        /// <summary>
        /// Frequency bin with the most energy, in Hz.
        /// </summary>
        public float DominantFrequency { get; }

        internal AudioSpectrum(float[] bands, float[] magnitudes, int fftSize, int sampleRate,
            float rms, float peak, float loudnessDb, float dominantFrequency)
        {
            Bands = bands;
            Magnitudes = magnitudes;
            FftSize = fftSize;
            SampleRate = sampleRate;
            Rms = rms;
            Peak = peak;
            LoudnessDb = loudnessDb;
            DominantFrequency = dominantFrequency;
        }

        /// <summary>
        /// Gets a normalized band value by index.
        /// </summary>
        public float this[int band] => Bands[band];

        /// <summary>
        /// Peak energy of bands between two fractions of the spectrum (0-1).
        /// Using the peak rather than the average keeps the value responsive to
        /// transients, which is what VFX/lighting sync needs.
        /// </summary>
        public float GetRange(float startFraction, float endFraction)
        {
            int a = Math.Clamp((int)(startFraction * Bands.Length), 0, Bands.Length - 1);
            int b = Math.Clamp((int)(endFraction * Bands.Length), a + 1, Bands.Length);
            float max = 0f;
            for (int i = a; i < b; i++) max = MathF.Max(max, Bands[i]);
            return max;
        }

        /// <summary>
        /// Low frequency energy (roughly 20-250 Hz).
        /// </summary>
        public float Bass => GetRange(0f, 0.25f);

        /// <summary>
        /// Mid frequency energy (roughly 250-2000 Hz).
        /// </summary>
        public float Mid => GetRange(0.25f, 0.65f);

        /// <summary>
        /// High frequency energy (roughly 2000 Hz and up).
        /// </summary>
        public float Treble => GetRange(0.65f, 1f);
    }
}
