# Audio

Division Engine's audio system is built on OpenAL (via Silk.NET) with a pure C#
decoding front-end. Everything is streamed through a single provider pipeline, so
decoded clips and procedural generators behave identically.

## Architecture

```
AudioAsset (.wav/.ogg) ──decode──> AudioClip ──> ClipProvider ─┐
                                                               ├─> AudioVoice ─> OpenAL
IAudioGenerator ──────────────────────────────> GeneratorProvider┘
```

| Type | Purpose |
| --- | --- |
| `AudioClip` | Decoded interleaved float samples, sample rate, channels, loop points |
| `AudioDecoderRegistry` | Picks a decoder by extension/magic; WAV and OGG Vorbis built in |
| `AudioSourceProvider` | Pull-based sample source (`ClipProvider`, `GeneratorProvider`) |
| `IAudioGenerator` | Implement this to synthesize audio procedurally |
| `AudioVoice` | One playing sound: volume, pitch, loop, 3D position, spectrum |
| `AudioEngine` | Static facade: init, listener, play helpers, per-frame pump |
| `AudioSystem` | ECS system that drives `AudioSource` components every frame |
| `AudioAnalyzer` | FFT, spectrum bands, RMS/peak, dominant frequency |

## Playing a sound from code

```csharp
// 2D one-shot
AudioVoice voice = AudioEngine.Play(clip, volume: 0.8f, pitch: 1f);

// 3D positional
AudioVoice voice = AudioEngine.PlayAt(clip, new float3(4, 0, 2),
    volume: 1f, minDistance: 2f, maxDistance: 40f);
```

`AudioEngine.Update()` must run once per frame - `AudioSystem` does this
automatically when a world is running.

## Using the ECS components

Add an **Audio Source** entity from the world window's context menu, then pick a
clip in the Properties window. Add an **Audio Listener** to any entity (or rely
on the active camera) to define where the "ears" are.

```csharp
AudioSource src = W.GetComponent<AudioSource>(entityId)!;
src.volume = 0.5f;
src.loop = true;
src.spatial = true;
```

Runtime control:

```csharp
AudioSystem.Instance?.Play(entityId);   // play even if playOnAwake is off
AudioSystem.Instance?.Stop(entityId);
bool playing = AudioSystem.Instance?.IsPlaying(entityId) ?? false;
```

## Procedural audio

Implement `IAudioGenerator` and play it like a clip:

```csharp
public sealed class KickGenerator : AudioGenerator
{
    public KickGenerator() : base(48000) { }
    public override void Generate(Span<float> destination, int frameCount) { /* ... */ }
    public override void Reset() { }
}

AudioEngine.PlayGenerator(new KickGenerator());
AudioEngine.PlayGeneratorAt(new KickGenerator(), position);
```

`SineGenerator` ships as a reference implementation.

## Spectrum analysis (VFX / lighting sync)

```csharp
AudioSpectrum spectrum = AudioSystem.Instance!.GetSpectrum(entityId)!;

float bass   = spectrum.Bass;              // 0-1
float energy = spectrum.GetRange(0f, 1f);  // peak energy across bands
float rms    = spectrum.Rms;
float hz     = spectrum.DominantFrequency;
```

Analysis is delayed by the backend's buffered frames, so it matches what the
listener actually hears rather than what was just queued.

## Formats

| Format | Decoder | Notes |
| --- | --- | --- |
| `.wav` | `WavDecoder` | PCM 8/16/24/32-bit, IEEE float 32/64-bit, WAVE_FORMAT_EXTENSIBLE |
| `.ogg` | `OggVorbisDecoder` | Via NVorbis |
| `.mp3` | - | Not supported yet; add an `IAudioDecoder` and register it |

Custom decoders: `AudioDecoderRegistry.Register(new MyDecoder());`

## Headless / no device

If no OpenAL device can be opened the engine logs a warning and falls back to
`NullAudioBackend`. Voices still advance in time, so gameplay logic depending on
sound completion keeps working without audio hardware.
