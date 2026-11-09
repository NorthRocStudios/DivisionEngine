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
    /// A single playing sound. Voices are created through
    /// <see cref="AudioEngine.CreateVoice"/> and are streamed from an
    /// <see cref="AudioSourceProvider"/>, so clips and procedural generators
    /// share one playback path.
    /// </summary>
    public abstract class AudioVoice : IDisposable
    {
        private AudioSourceProvider? provider;
        private readonly AnalysisTap analysisTap = new(8192);

        /// <summary>
        /// Raised when this voice has no more audio to stream.
        /// </summary>
        public event Action<AudioVoice>? Finished;

        /// <summary>
        /// The provider currently feeding this voice.
        /// </summary>
        public AudioSourceProvider? Provider => provider;

        /// <summary>
        /// Whether audio is currently being produced.
        /// </summary>
        public bool IsPlaying { get; protected set; }

        /// <summary>
        /// Whether the provider ran out of samples and playback ended.
        /// </summary>
        public bool IsFinished { get; protected set; }

        /// <summary>
        /// Per-voice volume (0-1).
        /// </summary>
        public float Volume { get; set; } = 1f;

        /// <summary>
        /// Playback pitch multiplier (1 = normal).
        /// </summary>
        public float Pitch { get; set; } = 1f;

        /// <summary>
        /// Whether playback wraps at the end of the source.
        /// </summary>
        public bool Loop { get; set; }

        /// <summary>
        /// Whether this voice is attenuated by 3D position.
        /// </summary>
        public bool Spatial { get; set; } = true;

        /// <summary>
        /// Distance at which the source is at full volume.
        /// </summary>
        public float MinDistance { get; set; } = 1f;

        /// <summary>
        /// Distance at which the source stops attenuating.
        /// </summary>
        public float MaxDistance { get; set; } = 50f;

        /// <summary>
        /// Distance attenuation curve.
        /// </summary>
        public AudioRolloff Rolloff { get; set; } = AudioRolloff.Inverse;

        /// <summary>
        /// World position of this voice (used when <see cref="Spatial"/> is true).
        /// </summary>
        public float3 Position { get; set; }

        /// <summary>
        /// World velocity of this voice, used for Doppler.
        /// </summary>
        public float3 Velocity { get; set; }

        /// <summary>
        /// Sample rate of the current provider.
        /// </summary>
        public int SampleRate => provider?.SampleRate ?? 48000;

        /// <summary>
        /// Channel count of the current provider.
        /// </summary>
        public int Channels => provider?.Channels ?? 1;

        /// <summary>
        /// Frames that have been written into backend buffers but not played yet.
        /// Subtracted from <see cref="Time"/> so it reports what the user hears.
        /// </summary>
        protected long BufferedFrames { get; set; }

        /// <summary>
        /// Playback position in seconds, compensated for backend buffering.
        /// </summary>
        public double Time => provider != null && provider.SampleRate > 0
            ? Math.Max(0L, provider.Position - BufferedFrames) / (double)provider.SampleRate
            : 0d;

        /// <summary>
        /// Starts playing the supplied provider, replacing anything currently playing.
        /// </summary>
        public void Play(AudioSourceProvider source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            StopStreaming();
            provider?.Dispose();

            provider = source;
            provider.Loop = Loop;
            analysisTap.Reset(source.SampleRate, source.Channels);

            IsFinished = false;
            long queuedFrames = StartStreaming();
            IsPlaying = queuedFrames > 0;
            if (!IsPlaying) IsFinished = true;
        }

        /// <summary>
        /// Stops playback and releases the provider.
        /// </summary>
        public virtual void Stop()
        {
            StopStreaming();
            provider?.Dispose();
            provider = null;
            IsPlaying = false;
            IsFinished = true;
            analysisTap.Reset(48000, 1);
        }

        /// <summary>
        /// Pauses playback without releasing the provider.
        /// </summary>
        public abstract void Pause();

        /// <summary>
        /// Pumps streaming buffers. Called by <see cref="AudioEngine.Update"/>.
        /// </summary>
        public abstract void Update();

        /// <summary>
        /// Marks the voice as finished and raises <see cref="Finished"/>.
        /// </summary>
        protected void MarkFinished()
        {
            if (IsFinished) return;
            IsPlaying = false;
            IsFinished = true;
            Finished?.Invoke(this);
        }

        /// <summary>
        /// Records samples for spectrum analysis. Called by backends as they stream.
        /// </summary>
        protected void TapAnalysis(ReadOnlySpan<float> interleaved, int frames) =>
            analysisTap.Write(interleaved, frames, Channels);

        /// <summary>
        /// Analyzes what this voice has recently produced. The window is delayed by
        /// the backend's buffered frames so it matches what the listener hears.
        /// </summary>
        public AudioSpectrum GetSpectrum(int fftSize = AudioAnalyzer.DefaultFftSize, int bands = AudioAnalyzer.DefaultBandCount)
        {
            int channels = Math.Max(1, Channels);
            return analysisTap.Analyze(SampleRate, channels, fftSize, bands, (int)Math.Min(BufferedFrames, int.MaxValue));
        }

        protected abstract long StartStreaming();

        protected abstract void StopStreaming();

        public abstract void Dispose();

        /// <summary>
        /// Rolling buffer of the most recent interleaved samples pushed to a voice.
        /// </summary>
        private sealed class AnalysisTap(int maxFrames)
        {
            private float[] buffer = [];
            private int channels = 1;
            private int frames;

            public void Reset(int sampleRate, int channelCount)
            {
                channels = Math.Max(1, channelCount);
                buffer = new float[maxFrames * channels];
                frames = 0;
            }

            public void Write(ReadOnlySpan<float> interleaved, int frameCount, int sourceChannels)
            {
                if (buffer.Length == 0) Reset(48000, sourceChannels);
                if (sourceChannels != channels || frameCount <= 0) return;

                int total = frameCount * channels;
                if (total >= buffer.Length)
                {
                    interleaved[^buffer.Length..].CopyTo(buffer);
                    frames = maxFrames;
                    return;
                }

                Array.Copy(buffer, total, buffer, 0, buffer.Length - total);
                interleaved[..total].CopyTo(buffer.AsSpan(buffer.Length - total));
                frames = Math.Min(maxFrames, frames + frameCount);
            }

            public AudioSpectrum Analyze(int sampleRate, int channelCount, int fftSize, int bandCount, int delayFrames = 0)
            {
                if (buffer.Length == 0) Reset(sampleRate, channelCount);
                if (!Fft.IsPowerOfTwo(fftSize)) fftSize = AudioAnalyzer.DefaultFftSize;

                int window = Math.Min(fftSize, maxFrames);
                int end = maxFrames - Math.Clamp(delayFrames, 0, Math.Max(0, maxFrames - window));
                int start = end - window;

                return AudioAnalyzer.Analyze(buffer.AsSpan(start * channels, window * channels), channels, sampleRate, fftSize, bandCount);
            }
        }
    }
}
