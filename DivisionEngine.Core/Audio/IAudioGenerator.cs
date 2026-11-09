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
    /// Produces audio samples on demand. Anything that implements this can be
    /// played through the engine exactly like a decoded clip by wrapping it in a
    /// <see cref="GeneratorProvider"/>, which is the foundation for procedural
    /// audio and, later, the procedural audio editor.
    /// </summary>
    public interface IAudioGenerator
    {
        /// <summary>
        /// Sample rate in Hz.
        /// </summary>
        int SampleRate { get; }

        /// <summary>
        /// Number of interleaved channels produced.
        /// </summary>
        int Channels { get; }

        /// <summary>
        /// Fills <paramref name="destination"/> with <paramref name="frameCount"/>
        /// frames of interleaved samples.
        /// </summary>
        void Generate(Span<float> destination, int frameCount);

        /// <summary>
        /// Resets generator state (phase, envelopes, etc).
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// Convenience base class with sane defaults for generators.
    /// </summary>
    public abstract class AudioGenerator : IAudioGenerator
    {
        public virtual int SampleRate { get; }

        public virtual int Channels => 1;

        protected AudioGenerator(int sampleRate = 48000)
        {
            SampleRate = sampleRate;
        }

        public abstract void Generate(Span<float> destination, int frameCount);

        public virtual void Reset() { }
    }

    /// <summary>
    /// A simple sine wave generator.
    /// </summary>
    public sealed class SineGenerator : AudioGenerator
    {
        private double phase;

        /// <summary>
        /// Frequency in Hz.
        /// </summary>
        public double Frequency { get; set; }

        /// <summary>
        /// Output amplitude (0-1).
        /// </summary>
        public float Amplitude { get; set; }

        public SineGenerator(double frequency = 440d, float amplitude = 0.25f, int sampleRate = 48000)
            : base(sampleRate)
        {
            Frequency = frequency;
            Amplitude = amplitude;
        }

        public override void Generate(Span<float> destination, int frameCount)
        {
            double step = 2d * Math.PI * Frequency / SampleRate;
            int channels = Channels;
            for (int frame = 0; frame < frameCount; frame++)
            {
                float value = (float)(Math.Sin(phase) * Amplitude);
                for (int ch = 0; ch < channels; ch++)
                    destination[frame * channels + ch] = value;
                phase += step;
                if (phase > 2d * Math.PI) phase -= 2d * Math.PI;
            }
        }

        public override void Reset() => phase = 0d;
    }
}
