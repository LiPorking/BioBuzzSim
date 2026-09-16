using System.Collections.Generic;
using UnityEngine;

// Match cues use the official FTC field audio (Resources/FieldSounds); the launch sound and the
// quiet drive-motor loop are synthesised.
public class SoundFx : MonoBehaviour
{
    static SoundFx inst;
    AudioSource cueSrc, fxSrc;           // field cues never share a source with effects (no pitch changes)
    public static System.Action<string> OnPlay;
    readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
    public static float Volume = 0.6f;
    public static AudioClip MotorClip { get; private set; }
    const int Rate = 22050;

    void Awake()
    {
        inst = this;
        cueSrc = gameObject.AddComponent<AudioSource>();
        cueSrc.playOnAwake = false;
        cueSrc.loop = false;
        fxSrc = gameObject.AddComponent<AudioSource>();
        fxSrc.playOnAwake = false;
        fxSrc.loop = false;
        // Official FIRST Tech Challenge field audio cues (from FTC Live), Table 9-1
        Load("start", "charge");                 // MATCH start - Cavalry Charge
        Load("endauto", "endauto");              // AUTO ends - Buzzer x 3
        Load("pickup", "Pick_Up_Controllers");   // AUTO to TELEOP transition
        Load("countdown", "3-2-1");
        Load("teleop", "firebell");              // TELEOP begins - 3 Bells
        Load("whistle", "factwhistle");          // Final 20 seconds - Train Whistle
        Load("endmatch", "endmatch");            // MATCH end - 3-second Buzzer
        Load("stopped", "fogblast");             // MATCH stopped - Foghorn
        clips["shot"] = MakeShot();
        MotorClip = MakeMotor();
    }

    void Load(string key, string file)
    {
        var c = Resources.Load<AudioClip>("FieldSounds/" + file);
        if (c) clips[key] = c;
    }

    public static float Length(string name) => inst != null && inst.clips.TryGetValue(name, out var c) ? c.length : 0f;

    delegate float Wave(float t);

    static AudioClip Make(float seconds, Wave w)
    {
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(w((float)i / Rate), -1, 1);
        var clip = AudioClip.Create("fx", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Field buzzer: a harsh, slightly detuned square-ish tone.
    static float Buzzer(float t)
    {
        float env = Mathf.Clamp01(t * 40f) * Mathf.Clamp01((1.3f - t) * 10f);
        float a = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 118f * t));
        float b = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 236.7f * t)) * 0.5f;
        float c = Mathf.Sin(2 * Mathf.PI * 354f * t) * 0.3f;
        return (a + b + c) * 0.22f * env;
    }

    // Foam ball squeezed through a flywheel: a soft low thump, a filtered "whump" of air and a
    // short rubbery squeak from the wheel.
    static AudioClip MakeShot()
    {
        float seconds = 0.32f;
        int n = Mathf.CeilToInt(seconds * Rate);
        var data = new float[n];
        var rng = new System.Random(7);
        float lp1 = 0, lp2 = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            lp1 += (noise - lp1) * 0.18f;          // low-passed noise (air)
            lp2 += (lp1 - lp2) * 0.25f;
            float thump = Mathf.Sin(2 * Mathf.PI * (95f - 40f * t / seconds) * t) * Mathf.Exp(-t * 28f);
            float air = lp2 * 2.2f * Mathf.Exp(-t * 16f) * Mathf.Clamp01(t * 200f);
            float squeak = Mathf.Sin(2 * Mathf.PI * (620f + 900f * t) * t) * 0.08f * Mathf.Exp(-t * 60f);
            data[i] = Mathf.Clamp((thump * 0.75f + air + squeak) * 0.8f, -1, 1);
        }
        var clip = AudioClip.Create("shot", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Seamless loop of a small DC gear motor: whine + gear harmonics + a little brush noise.
    static AudioClip MakeMotor()
    {
        const float f = 110f;               // 1 s loop, integer multiples of 1 Hz loop cleanly
        int n = Rate;
        var data = new float[n];
        var rng = new System.Random(3);
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            lp += (noise - lp) * 0.3f;
            float whine = Mathf.Sin(2 * Mathf.PI * f * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * f * 2 * t) * 0.25f
                        + Mathf.Sin(2 * Mathf.PI * f * 3 * t) * 0.12f + Mathf.Sin(2 * Mathf.PI * 740f * t) * 0.06f;
            float ripple = 0.85f + 0.15f * Mathf.Sin(2 * Mathf.PI * 22f * t);
            data[i] = (whine * ripple + lp * 0.25f) * 0.5f;
        }
        var clip = AudioClip.Create("motor", n, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Silence any field cue still playing (used when a match is restarted).
    public static void StopCues()
    {
        if (inst != null) inst.cueSrc.Stop();
    }

    public static void Play(string name)
    {
        if (inst == null || !inst.clips.TryGetValue(name, out var c)) return;
        if (inst.lastPlay.TryGetValue(name, out var lp) && Time.unscaledTime - lp < 0.04f) return;
        inst.lastPlay[name] = Time.unscaledTime;
        OnPlay?.Invoke(name);
        if (name == "shot")
        {
            inst.fxSrc.pitch = Random.Range(0.95f, 1.05f);
            inst.fxSrc.PlayOneShot(c, Volume * 0.7f);
        }
        else
        {
            inst.cueSrc.pitch = 1f;
            inst.cueSrc.PlayOneShot(c, Volume);
        }
    }
}
