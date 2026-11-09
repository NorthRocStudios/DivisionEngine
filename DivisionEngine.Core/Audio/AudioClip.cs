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
    /// Decoded audio data held in memory as interleaved 32-bit float samples.
    /// All channels are interleaved frame by frame: L R L R ...
    /// </summary>
    public sealed class AudioClip
    {
        /// <summary>
        /// Display name of this clip (usually the source file name).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Sample rate in Hz.
        /// </summary>
        public int SampleRate { get; }

        /// <summary>
        /// Number of interleaved channels (1 = mono, 2 = stereo).
        /// </summary>
        public int Channels { get; }

        /// <summary>
        /// Interleaved float samples in the range [-1, 1].
        /// </summary>
        public float[] Samples { get; }

        /// <summary>
        /// Number of frames (samples per channel).
        /// </summary>
        public int FrameCount => Channels > 0 ? Samples.Length / Channels : 0;

        /// <summary>
        /// Length of the clip in seconds.
        /// </summary>
        public double Duration => SampleRate > 0 ? FrameCount / (double)SampleRate : 0d;

        /// <summary>
        /// First frame played when looping, in frames.
        /// </summary>
        public int LoopStartFrame { get; set; }

        /// <summary>
        /// Frame the loop returns to (exclusive end), in frames.
        /// </summary>
        public int LoopEndFrame { get; set; }

        /// <summary>
        /// Creates a new audio clip.
        /// </summary>
        public AudioClip(string name, int sampleRate, int channels, float[] samples)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (samples.Length % channels != 0)
                throw new ArgumentException("Sample count must be a multiple of the channel count.", nameof(samples));

            Name = name;
            SampleRate = sampleRate;
            Channels = channels;
            Samples = samples;
            LoopStartFrame = 0;
            LoopEndFrame = samples.Length / channels;
        }

        /// <summary>
        /// Creates a silent clip of the given length.
        /// </summary>
        public static AudioClip Silence(string name, int sampleRate, int channels, double seconds)
        {
            int frames = Math.Max(1, (int)(sampleRate * seconds));
            return new AudioClip(name, sampleRate, channels, new float[frames * channels]);
        }

        /// <summary>
        /// Creates a mono sine wave clip. Useful for tests and procedural audio.
        /// </summary>
        public static AudioClip Sine(string name, int sampleRate, double frequency, double seconds, float amplitude = 0.5f)
        {
            int frames = Math.Max(1, (int)(sampleRate * seconds));
            float[] samples = new float[frames];
            double step = 2d * Math.PI * frequency / sampleRate;
            for (int i = 0; i < frames; i++)
                samples[i] = (float)(Math.Sin(step * i) * amplitude);
            return new AudioClip(name, sampleRate, 1, samples);
        }

        /// <summary>
        /// Peak absolute sample value in the clip.
        /// </summary>
        public float Peak()
        {
            float peak = 0f;
            foreach (float s in Samples) peak = Math.Max(peak, Math.Abs(s));
            return peak;
        }
    }
}
