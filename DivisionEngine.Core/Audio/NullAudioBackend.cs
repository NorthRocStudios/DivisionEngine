//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using System.Diagnostics;

namespace DivisionEngine.Audio
{
    /// <summary>
    /// Silent backend used when no audio device is available (headless runs, CI,
    /// machines with no output). Voices still advance in time so game logic that
    /// waits for sounds to finish behaves the same.
    /// </summary>
    public sealed class NullAudioBackend : AudioBackend
    {
        public override string Name => "Null (silent)";

        public override bool Initialize()
        {
            IsAvailable = false;
            return true;
        }

        public override void Shutdown() => IsAvailable = false;

        public override AudioVoice CreateVoice() => new NullVoice();

        public override void SetMasterGain(float gain) { }

        public override void SetListener(in AudioListenerState state) { }

        private sealed class NullVoice : AudioVoice
        {
            private long startTicks;
            private long startFrame;

            public override void Update()
            {
                if (!IsPlaying || Provider == null) return;

                double elapsed = (Stopwatch.GetTimestamp() - startTicks) / (double)Stopwatch.Frequency;
                long advanced = startFrame + (long)(elapsed * Provider.SampleRate * Pitch);

                if (Provider.FrameCount > 0)
                {
                    if (Loop && Provider.FrameCount > 0)
                        Provider.Position = advanced % Provider.FrameCount;
                    else if (advanced >= Provider.FrameCount)
                    {
                        Provider.Position = Provider.FrameCount;
                        MarkFinished();
                        return;
                    }
                    else Provider.Position = advanced;
                }
            }

            protected override long StartStreaming()
            {
                startTicks = Stopwatch.GetTimestamp();
                startFrame = Provider?.Position ?? 0;
                return 1;
            }

            protected override void StopStreaming()
            {
                IsPlaying = false;
            }

            public override void Pause() => IsPlaying = false;

            public override void Dispose() => Stop();
        }
    }
}
