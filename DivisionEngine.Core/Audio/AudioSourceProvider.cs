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
    /// A source of audio samples that can be pulled by the audio engine.
    /// Implemented by decoded clips (<see cref="ClipProvider"/>) and by
    /// procedural generators (<see cref="GeneratorProvider"/>), so synthesized
    /// audio and file audio use exactly the same playback path.
    /// </summary>
    public abstract class AudioSourceProvider : IDisposable
    {
        /// <summary>
        /// Sample rate in Hz.
        /// </summary>
        public abstract int SampleRate { get; }

        /// <summary>
        /// Number of interleaved channels.
        /// </summary>
        public abstract int Channels { get; }

        /// <summary>
        /// Total number of frames, or -1 when the source is infinite (procedural).
        /// </summary>
        public virtual long FrameCount => -1;

        /// <summary>
        /// Whether playback wraps when the end of the source is reached.
        /// </summary>
        public virtual bool Loop { get; set; }

        /// <summary>
        /// Current playback position in frames.
        /// </summary>
        public abstract long Position { get; set; }

        /// <summary>
        /// Fills <paramref name="destination"/> with up to <paramref name="frameCount"/>
        /// frames of interleaved samples.
        /// </summary>
        /// <returns>Number of frames actually written (0 = source exhausted).</returns>
        public abstract int Read(Span<float> destination, int frameCount);

        /// <summary>
        /// Resets playback back to the start.
        /// </summary>
        public virtual void Reset() => Position = 0;

        public virtual void Dispose() { }
    }

    /// <summary>
    /// Plays a decoded <see cref="AudioClip"/> with optional looping.
    /// </summary>
    public sealed class ClipProvider(AudioClip clip) : AudioSourceProvider
    {
        private readonly AudioClip clip = clip ?? throw new ArgumentNullException(nameof(clip));
        private long position;

        public override int SampleRate => clip.SampleRate;

        public override int Channels => clip.Channels;

        public override long FrameCount => clip.FrameCount;

        public override long Position
        {
            get => position;
            set => position = Math.Clamp(value, 0L, clip.FrameCount);
        }

        public override int Read(Span<float> destination, int frameCount)
        {
            long loopStart = Math.Clamp(clip.LoopStartFrame, 0, clip.FrameCount);
            long loopEnd = Math.Clamp(clip.LoopEndFrame, loopStart, clip.FrameCount);
            if (loopEnd <= loopStart) loopEnd = clip.FrameCount;

            int channels = clip.Channels;
            int written = 0;

            while (written < frameCount)
            {
                long remaining = loopEnd - position;
                if (remaining <= 0)
                {
                    if (!Loop) break;
                    position = loopStart;
                    remaining = loopEnd - loopStart;
                    if (remaining <= 0) break;
                }

                int frames = (int)Math.Min(frameCount - written, remaining);
                clip.Samples.AsSpan((int)position * channels, frames * channels)
                    .CopyTo(destination.Slice(written * channels));

                position += frames;
                written += frames;
            }

            return written;
        }
    }

    /// <summary>
    /// Pulls samples from an <see cref="IAudioGenerator"/>, producing an infinite
    /// (or generator-bounded) stream. This is the entry point for procedural audio.
    /// </summary>
    public sealed class GeneratorProvider(IAudioGenerator generator) : AudioSourceProvider
    {
        private readonly IAudioGenerator generator = generator ?? throw new ArgumentNullException(nameof(generator));
        private long position;

        public override int SampleRate => generator.SampleRate;

        public override int Channels => generator.Channels;

        public override bool Loop
        {
            get => false;
            set { }
        }

        public override long Position
        {
            get => position;
            set => position = Math.Max(0L, value);
        }

        public override int Read(Span<float> destination, int frameCount)
        {
            generator.Generate(destination[..(frameCount * generator.Channels)], frameCount);
            position += frameCount;
            return frameCount;
        }
    }
}
