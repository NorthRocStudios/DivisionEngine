//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.MathLib;
using Silk.NET.OpenAL;

namespace DivisionEngine.Audio
{
    /// <summary>
    /// OpenAL playback backend. Uses the system OpenAL runtime (OpenAL32 on
    /// Windows, libopenal elsewhere); if a device cannot be opened the engine
    /// falls back to <see cref="NullAudioBackend"/> so the editor never crashes.
    /// </summary>
    public sealed unsafe class OpenALBackend : AudioBackend
    {
        private ALContext? contextApi;
        private AL? alApi;
        private Device* device;
        private Context* context;
        private bool ownsApi;

        public override string Name => "OpenAL";

        internal AL AL => alApi ?? throw new InvalidOperationException("OpenAL backend is not initialized.");

        public override bool Initialize()
        {
            if (IsAvailable) return true;

            try
            {
                alApi = AL.GetApi(false);
                contextApi = ALContext.GetApi(false);
                ownsApi = true;

                device = contextApi.OpenDevice(null);
                if (device == null)
                {
                    Debug.Warning("Audio: OpenAL could not open an output device.");
                    return false;
                }

                context = contextApi.CreateContext(device, (int*)null);
                if (context == null)
                {
                    Debug.Warning("Audio: OpenAL could not create a context.");
                    return false;
                }

                contextApi.MakeContextCurrent(context);
                alApi.DistanceModel(DistanceModel.InverseDistance);

                IsAvailable = true;
                Debug.Info("Audio: OpenAL initialized.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Warning("Audio: OpenAL initialization failed, audio will be silent.", ex);
                IsAvailable = false;
                return false;
            }
        }

        public override void Shutdown()
        {
            if (!IsAvailable && alApi == null) return;

            try
            {
                if (contextApi != null)
                {
                    contextApi.MakeContextCurrent((Context*)null);
                    if (context != null) contextApi.DestroyContext(context);
                    if (device != null) contextApi.CloseDevice(device);
                }
            }
            catch (Exception ex)
            {
                Debug.Warning("Audio: error while shutting down OpenAL", ex);
            }
            finally
            {
                context = null;
                device = null;
                IsAvailable = false;

                if (ownsApi)
                {
                    alApi?.Dispose();
                    contextApi?.Dispose();
                }
                alApi = null;
                contextApi = null;
            }
        }

        public override AudioVoice CreateVoice() => new OpenALVoice(this);

        public override void MakeCurrent()
        {
            if (IsAvailable && contextApi != null && context != null)
                contextApi.MakeContextCurrent(context);
        }

        public override void SetMasterGain(float gain)
        {
            if (!IsAvailable || alApi == null) return;
            alApi.SetListenerProperty(ListenerFloat.Gain, Math.Clamp(gain, 0f, 1f));
        }

        public override void SetListener(in AudioListenerState state)
        {
            if (!IsAvailable || alApi == null) return;

            alApi.SetListenerProperty(ListenerVector3.Position, state.Position.X, state.Position.Y, state.Position.Z);
            alApi.SetListenerProperty(ListenerVector3.Velocity, state.Velocity.X, state.Velocity.Y, state.Velocity.Z);

            float* orientation = stackalloc float[6]
            {
                state.Forward.X, state.Forward.Y, state.Forward.Z,
                state.Up.X, state.Up.Y, state.Up.Z,
            };
            alApi.SetListenerProperty(ListenerFloatArray.Orientation, orientation);
        }

        public override void Dispose()
        {
            Shutdown();
            GC.SuppressFinalize(this);
        }
    }
}

