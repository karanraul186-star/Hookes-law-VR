using UnityEngine;

namespace HookesLawLab.Lab
{
    /// <summary>
    /// Synthesizes procedural AudioClips (sine/chords) for tactile lab interactions without external SFX assets.
    /// </summary>
    public static class LabAudioSynthesizer
    {
        private const int SampleRate = 22050;

        public static AudioClip GetToneClip(float frequency, float durationSec = 0.10f, float gain = 0.16f)
        {
            int count = Mathf.Max(64, Mathf.RoundToInt(SampleRate * durationSec));
            float[] data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float env = Mathf.Exp(-4.5f * (t / durationSec));
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * gain * env;
            }
            AudioClip clip = AudioClip.Create($"Tone_{frequency:F0}", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static AudioClip GetChordClip(float f1, float f2, float f3, float durationSec = 0.22f, float gain = 0.14f)
        {
            int count = Mathf.Max(64, Mathf.RoundToInt(SampleRate * durationSec));
            float[] data = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float s = 0f;
                if (f1 > 0f) s += Mathf.Sin(2f * Mathf.PI * f1 * t) * Mathf.Exp(-6f * t);
                if (f2 > 0f && t > 0.035f) s += Mathf.Sin(2f * Mathf.PI * f2 * (t - 0.035f)) * Mathf.Exp(-6f * (t - 0.035f));
                if (f3 > 0f && t > 0.07f) s += Mathf.Sin(2f * Mathf.PI * f3 * (t - 0.07f)) * Mathf.Exp(-5f * (t - 0.07f));
                data[i] = Mathf.Clamp(s * gain, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("ChordClip", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
