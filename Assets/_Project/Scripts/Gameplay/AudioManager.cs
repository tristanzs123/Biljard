using Billiards.Core;
using UnityEngine;

namespace Billiards.Gameplay
{
    /// <summary>
    /// Game sounds generated in code (no audio files needed): ball-ball click, cushion thud,
    /// pocket drop and cue strike. Volume follows the impact speed and GameSettings.Volume.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        [Tooltip("Number of sounds that can play at the same time.")]
        [SerializeField, Range(2, 32)] int voices = 12;

        [Tooltip("Impact speed (m/s) that plays at full volume.")]
        [SerializeField] float fullVolumeSpeed = 4f;

        AudioSource[] sources;
        int next;
        AudioClip click, cushion, pocket, cueHit;
        CueController cue;
        float lastClickTime;

        void Awake()
        {
            GameSettings.EnsureLoaded();
            sources = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].spatialBlend = 0f;
            }
            const int rate = 44100;
            click = MakeClick(rate);
            cushion = MakeThud(rate);
            pocket = MakePocket(rate);
            cueHit = MakeCueHit(rate);
        }

        void OnEnable()
        {
            Ball.BallCollision += OnBallCollision;
            Ball.CushionHit += OnCushion;
            Ball.Pocketed += OnPocketed;
        }

        void OnDisable()
        {
            Ball.BallCollision -= OnBallCollision;
            Ball.CushionHit -= OnCushion;
            Ball.Pocketed -= OnPocketed;
            if (cue != null) cue.ShotFired -= OnShot;
        }

        void Start()
        {
            cue = FindFirstObjectByType<CueController>(FindObjectsInactive.Include);
            if (cue != null) cue.ShotFired += OnShot;
        }

        void OnBallCollision(Ball a, float speed)
        {
            if (Time.time - lastClickTime < 0.012f) return; // avoid a wall of sound in the break
            lastClickTime = Time.time;
            Play(click, Loudness(speed), Random.Range(0.92f, 1.08f));
        }

        void OnCushion(Ball b)
        {
            float speed = b.Body != null ? b.Body.linearVelocity.magnitude : 1f;
            Play(cushion, Loudness(speed) * 0.8f, Random.Range(0.9f, 1.1f));
        }

        void OnPocketed(Ball b) => Play(pocket, 0.9f, Random.Range(0.95f, 1.05f));

        void OnShot(Vector3 dir, float power) => Play(cueHit, 0.35f + 0.65f * power, Random.Range(0.97f, 1.03f));

        float Loudness(float speed) => Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(0f, speed) / fullVolumeSpeed));

        void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || volume < 0.02f) return;
            AudioSource s = sources[next];
            next = (next + 1) % sources.Length;
            s.pitch = pitch;
            s.volume = volume;
            s.clip = clip;
            s.Play();
        }

        // ------------------------------------------------------------------ synthesis

        /// <summary>Phenolic ball click: short bright tone with a fast decay and a bit of noise.</summary>
        static AudioClip MakeClick(int rate)
        {
            int n = (int)(rate * 0.09f);
            float[] d = new float[n];
            var rnd = new System.Random(1);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 70f);
                float tone = Mathf.Sin(2f * Mathf.PI * 3100f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 5400f * t) * 0.25f;
                float noise = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 400f);
                d[i] = (tone * env + noise * 0.5f) * 0.8f;
            }
            return Clip("click", d, rate);
        }

        /// <summary>Cushion: soft low thump.</summary>
        static AudioClip MakeThud(int rate)
        {
            int n = (int)(rate * 0.16f);
            float[] d = new float[n];
            var rnd = new System.Random(2);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 35f);
                float tone = Mathf.Sin(2f * Mathf.PI * 140f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * 290f * t);
                float noise = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 180f) * 0.4f;
                d[i] = (tone * env * 0.7f + noise) * 0.8f;
            }
            return Clip("cushion", d, rate);
        }

        /// <summary>Pocket: thud into the pocket followed by a short rattle.</summary>
        static AudioClip MakePocket(int rate)
        {
            int n = (int)(rate * 0.45f);
            float[] d = new float[n];
            var rnd = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float thud = Mathf.Sin(2f * Mathf.PI * 95f * t) * Mathf.Exp(-t * 18f);
                float rattle = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float t0 = 0.08f + k * 0.07f;
                    if (t > t0) rattle += Mathf.Sin(2f * Mathf.PI * (1800f - k * 300f) * (t - t0)) * Mathf.Exp(-(t - t0) * 90f) * (0.5f - k * 0.12f);
                }
                float noise = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f) * 0.25f;
                d[i] = (thud * 0.8f + rattle + noise) * 0.7f;
            }
            return Clip("pocket", d, rate);
        }

        /// <summary>Cue tip on the cue ball: dull wooden knock.</summary>
        static AudioClip MakeCueHit(int rate)
        {
            int n = (int)(rate * 0.1f);
            float[] d = new float[n];
            var rnd = new System.Random(4);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 60f);
                float tone = Mathf.Sin(2f * Mathf.PI * 900f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 1650f * t) * 0.3f;
                float noise = ((float)rnd.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 300f) * 0.6f;
                d[i] = (tone * env + noise) * 0.8f;
            }
            return Clip("cue", d, rate);
        }

        static AudioClip Clip(string name, float[] data, int rate)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
