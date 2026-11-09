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
    /// Turns interleaved audio into an <see cref="AudioSpectrum"/>: FFT magnitudes,
    /// normalized frequency bands, RMS/peak level and dominant frequency.
    /// </summary>
    public static class AudioAnalyzer
    {
        /// <summary>
        /// Default FFT window size in samples.
        /// </summary>
        public const int DefaultFftSize = 2048;

        /// <summary>
        /// Default number of log-spaced frequency bands.
        /// </summary>
        public const int DefaultBandCount = 32;

        /// <summary>
        /// Lowest frequency represented by the band layout, in Hz.
        /// </summary>
        public const float MinFrequency = 20f;

        /// <summary>
        /// Magnitudes are mapped from this floor up to 0 dB to produce 0-1 band values.
        /// </summary>
        public const float MagnitudeFloorDb = -80f;

        /// <summary>
        /// Analyzes interleaved audio. The most recent <paramref name="fftSize"/> frames
        /// are used; if fewer are available the window is zero padded.
        /// </summary>
        public static AudioSpectrum Analyze(ReadOnlySpan<float> interleaved, int channels, int sampleRate,
            int fftSize = DefaultFftSize, int bandCount = DefaultBandCount, FftWindow window = FftWindow.Hann)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (!Fft.IsPowerOfTwo(fftSize)) fftSize = DefaultFftSize;
            if (bandCount <= 0) bandCount = DefaultBandCount;

            float[] mono = Downmix(interleaved, channels, fftSize);
            float[] magnitudes = new float[fftSize / 2];
            Fft.Magnitudes(mono, magnitudes, window);

            float sumSquares = 0f;
            float peak = 0f;
            foreach (float s in mono)
            {
                sumSquares += s * s;
                peak = MathF.Max(peak, MathF.Abs(s));
            }

            float rms = MathF.Sqrt(sumSquares / mono.Length);
            float loudness = 20f * MathF.Log10(MathF.Max(rms, 1e-6f));

            int dominantBin = 0;
            for (int i = 1; i < magnitudes.Length; i++)
                if (magnitudes[i] > magnitudes[dominantBin]) dominantBin = i;
            float dominantFrequency = dominantBin * sampleRate / (float)fftSize;

            float[] bands = ComputeBands(magnitudes, sampleRate, fftSize, bandCount);

            return new AudioSpectrum(bands, magnitudes, fftSize, sampleRate, rms, peak, loudness, dominantFrequency);
        }

        /// <summary>
        /// Analyzes a decoded clip at a given time offset.
        /// </summary>
        public static AudioSpectrum AnalyzeClip(AudioClip clip, double timeSeconds,
            int fftSize = DefaultFftSize, int bandCount = DefaultBandCount)
        {
            int channels = clip.Channels;
            int frames = Math.Min(fftSize, clip.FrameCount);
            int startFrame = Math.Clamp((int)(timeSeconds * clip.SampleRate) - frames, 0, Math.Max(0, clip.FrameCount - frames));

            ReadOnlySpan<float> window = clip.Samples.AsSpan(startFrame * channels, frames * channels);
            return Analyze(window, channels, clip.SampleRate, fftSize, bandCount);
        }

        private static float[] Downmix(ReadOnlySpan<float> interleaved, int channels, int fftSize)
        {
            float[] mono = new float[fftSize];
            int availableFrames = interleaved.Length / channels;
            int frames = Math.Min(availableFrames, fftSize);
            int offset = fftSize - frames;

            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                for (int ch = 0; ch < channels; ch++)
                    sum += interleaved[frame * channels + ch];
                mono[offset + frame] = sum / channels;
            }

            return mono;
        }

        private static float[] ComputeBands(float[] magnitudes, int sampleRate, int fftSize, int bandCount)
        {
            float[] bands = new float[bandCount];
            float nyquist = sampleRate * 0.5f;
            float maxFrequency = MathF.Min(nyquist, 20000f);
            if (maxFrequency <= MinFrequency) maxFrequency = nyquist;

            float ratio = maxFrequency / MinFrequency;
            float binHz = sampleRate / (float)fftSize;

            for (int band = 0; band < bandCount; band++)
            {
                float lowHz = MinFrequency * MathF.Pow(ratio, band / (float)bandCount);
                float highHz = MinFrequency * MathF.Pow(ratio, (band + 1) / (float)bandCount);

                int lowBin = Math.Clamp((int)(lowHz / binHz), 0, magnitudes.Length - 1);
                int highBin = Math.Clamp((int)(highHz / binHz), lowBin + 1, magnitudes.Length);

                float max = 0f;
                for (int bin = lowBin; bin < highBin; bin++)
                    max = MathF.Max(max, magnitudes[bin]);

                float db = 20f * MathF.Log10(MathF.Max(max, 1e-6f));
                bands[band] = Math.Clamp((db - MagnitudeFloorDb) / -MagnitudeFloorDb, 0f, 1f);
            }

            return bands;
        }
    }
}
