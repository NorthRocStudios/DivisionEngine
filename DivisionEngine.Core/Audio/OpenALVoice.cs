//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Silk.NET.OpenAL;

namespace DivisionEngine.Audio
{
    /// <summary>
    /// Streaming OpenAL voice. Samples are pulled from the voice's provider into
    /// a small ring of buffers, which keeps clips and procedural generators on
    /// exactly the same path.
    /// </summary>
    internal sealed unsafe class OpenALVoice : AudioVoice
    {
        private const int BufferCount = 3;
        private const int FramesPerBuffer = 2048;

        private readonly AL al;
        private readonly uint source;
        private readonly uint[] buffers;
        private readonly int[] bufferFrames;
        private float[] scratch = [];
        private short[] pcm = [];
        private int queued;
        private bool disposed;

        public OpenALVoice(OpenALBackend backend)
        {
            al = backend.AL;
            source = al.GenSource();
            buffers = al.GenBuffers(BufferCount);
            bufferFrames = new int[BufferCount];
            al.SetSourceProperty(source, SourceFloat.Gain, 1f);
        }

        protected override long StartStreaming()
        {
            EnsureScratch();
            queued = 0;
            BufferedFrames = 0;
            long frames = 0;

            for (int i = 0; i < buffers.Length; i++)
            {
                int filled = FillBuffer(buffers[i]);
                if (filled <= 0) break;

                bufferFrames[i] = filled;
                uint buffer = buffers[i];
                al.SourceQueueBuffers(source, 1, &buffer);
                queued++;
                BufferedFrames += filled;
                frames += filled;
            }

            if (queued > 0) al.SourcePlay(source);
            return frames;
        }

        protected override void StopStreaming()
        {
            if (source == 0) return;

            al.SourceStop(source);

            int pending = 0;
            al.GetSourceProperty(source, GetSourceInteger.BuffersQueued, out pending);
            while (pending-- > 0)
            {
                uint buffer;
                al.SourceUnqueueBuffers(source, 1, &buffer);
            }

            queued = 0;
            BufferedFrames = 0;
        }

        public override void Update()
        {
            if (disposed || source == 0) return;

            ApplyProperties();

            if (!IsPlaying || Provider == null) return;

            int processed = 0;
            al.GetSourceProperty(source, GetSourceInteger.BuffersProcessed, out processed);

            while (processed-- > 0)
            {
                uint buffer;
                al.SourceUnqueueBuffers(source, 1, &buffer);
                if (queued > 0) queued--;

                int index = Array.IndexOf(buffers, buffer);
                if (index >= 0) BufferedFrames = Math.Max(0, BufferedFrames - bufferFrames[index]);

                int filled = FillBuffer(buffer);
                if (filled > 0)
                {
                    if (index >= 0) bufferFrames[index] = filled;
                    uint queuedBuffer = buffer;
                    al.SourceQueueBuffers(source, 1, &queuedBuffer);
                    queued++;
                    BufferedFrames += filled;
                }
            }

            if (queued == 0)
            {
                al.SourceStop(source);
                BufferedFrames = 0;
                MarkFinished();
                return;
            }

            al.GetSourceProperty(source, GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing) al.SourcePlay(source);
        }

        public override void Pause()
        {
            if (disposed || source == 0) return;
            al.SourcePause(source);
            IsPlaying = false;
        }

        private void ApplyProperties()
        {
            AudioSourceProvider? provider = Provider;
            if (provider == null) return;

            provider.Loop = Loop;

            al.SetSourceProperty(source, SourceFloat.Gain, Math.Clamp(Volume, 0f, 4f));
            al.SetSourceProperty(source, SourceFloat.Pitch, Math.Clamp(Pitch, 0.1f, 4f));
            al.SetSourceProperty(source, SourceBoolean.Looping, false);
            al.SetSourceProperty(source, SourceBoolean.SourceRelative, !Spatial);

            if (Spatial)
            {
                al.SetSourceProperty(source, SourceVector3.Position, Position.x, Position.y, Position.z);
                al.SetSourceProperty(source, SourceVector3.Velocity, Velocity.x, Velocity.y, Velocity.z);
            }

            al.SetSourceProperty(source, SourceFloat.ReferenceDistance, MathF.Max(0.01f, MinDistance));
            al.SetSourceProperty(source, SourceFloat.MaxDistance, MathF.Max(MinDistance, MaxDistance));
            al.SetSourceProperty(source, SourceFloat.RolloffFactor, RolloffFactor());
        }

        private float RolloffFactor() => Rolloff switch
        {
            AudioRolloff.None => 0f,
            AudioRolloff.Linear => 1f,
            AudioRolloff.Exponent => 2f,
            _ => 1f,
        };

        private void EnsureScratch()
        {
            int channels = Math.Max(1, Channels);
            int needed = FramesPerBuffer * channels;
            if (scratch.Length < needed) scratch = new float[needed];
            if (pcm.Length < needed * 2) pcm = new short[needed * 2];
        }

        private int FillBuffer(uint buffer)
        {
            AudioSourceProvider? provider = Provider;
            if (provider == null) return 0;

            EnsureScratch();

            int channels = provider.Channels;
            int frames = provider.Read(scratch.AsSpan(), FramesPerBuffer);
            if (frames <= 0) return 0;

            TapAnalysis(scratch.AsSpan(0, frames * channels), frames);

            BufferFormat format = channels == 1 ? BufferFormat.Mono16 : BufferFormat.Stereo16;
            int sampleCount = ConvertToPcm(scratch.AsSpan(0, frames * channels), frames, channels);
            short[] upload = pcm[..sampleCount];
            al.BufferData(buffer, format, upload, provider.SampleRate);
            return frames;
        }

        private int ConvertToPcm(ReadOnlySpan<float> source, int frames, int channels)
        {
            if (channels == 1)
            {
                for (int i = 0; i < frames; i++) pcm[i] = ToPcm16(source[i]);
                return frames;
            }

            if (channels == 2)
            {
                int count = frames * 2;
                for (int i = 0; i < count; i++) pcm[i] = ToPcm16(source[i]);
                return count;
            }

            // Downmix >2 channels into stereo pairs.
            int even = (channels + 1) / 2;
            int odd = Math.Max(1, channels / 2);
            for (int frame = 0; frame < frames; frame++)
            {
                float left = 0f, right = 0f;
                for (int ch = 0; ch < channels; ch++)
                {
                    float value = source[frame * channels + ch];
                    if ((ch & 1) == 0) left += value;
                    else right += value;
                }
                pcm[frame * 2] = ToPcm16(left / even);
                pcm[frame * 2 + 1] = ToPcm16(right / odd);
            }
            return frames * 2;
        }

        private static short ToPcm16(float value) =>
            (short)Math.Clamp((int)(value * 32767f), short.MinValue, short.MaxValue);

        public override void Dispose()
        {
            if (disposed) return;
            disposed = true;

            try
            {
                StopStreaming();
                IsPlaying = false;

                // Mark finished as well: AudioEngine evicts list entries by IsFinished, so a
                // voice disposed elsewhere would otherwise stay in the list forever.
                IsFinished = true;

                if (source != 0)
                {
                    al.DeleteSource(source);
                    al.DeleteBuffers(buffers);
                }
            }
            catch (Exception ex)
            {
                Debug.Warning("Audio: failed to dispose OpenAL voice", ex);
            }
        }
    }
}



