using System.Collections;
using UnityEngine;

namespace Strategy
{
    /// <summary>
    /// Background music. Clips are loaded from Resources/TempOST, which holds placeholder tracks:
    /// Gameplay/ is shuffled during play, Victory/ and Defeat/ play on the game-over screen.
    /// Delete that folder and the game runs silent. Starts muted; the HUD's ♪ button or M toggles it (remembered between runs).
    /// </summary>
    public class Music : MonoBehaviour
    {
        const string Root = "TempOST/";
        const float Volume = 0.45f, Fade = 1.5f;
        const string MuteKey = "MusicMuted";

        readonly AudioSource[] src = new AudioSource[2];
        AudioClip[] gameplay, victory, defeat;
        int[] order;
        int cur, next;
        bool playlist;
        Coroutine fade;

        public bool Muted
        {
            get => src[0].mute;
            set
            {
                foreach (var s in src) s.mute = value;
                PlayerPrefs.SetInt(MuteKey, value ? 1 : 0);
            }
        }

        void Awake()
        {
            gameplay = Resources.LoadAll<AudioClip>(Root + "Gameplay");
            victory = Resources.LoadAll<AudioClip>(Root + "Victory");
            defeat = Resources.LoadAll<AudioClip>(Root + "Defeat");
            for (int i = 0; i < 2; i++)
            {
                src[i] = gameObject.AddComponent<AudioSource>();
                src[i].playOnAwake = false;
                src[i].spatialBlend = 0;
            }
            // Starts muted until the player turns it on.
            Muted = PlayerPrefs.GetInt(MuteKey, 1) == 1;
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
        }

        public void PlayGameplay()
        {
            playlist = gameplay.Length > 0;
            if (!playlist) { CrossTo(null, false); return; }
            order = new int[gameplay.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            next = 0;
            PlayNext();
        }

        public void PlayEnd(bool won)
        {
            playlist = false;
            var set = won ? victory : defeat;
            CrossTo(set.Length > 0 ? set[Random.Range(0, set.Length)] : null, !won);
        }

        void Update()
        {
            // Start the next track while the current one is still fading out, so the playlist never goes quiet.
            var a = src[cur];
            if (playlist && fade == null && a.clip != null && a.isPlaying && a.time >= a.clip.length - Fade) PlayNext();
        }

        void PlayNext()
        {
            CrossTo(gameplay[order[next]], false);
            next = (next + 1) % order.Length;
        }

        void CrossTo(AudioClip clip, bool loop)
        {
            var a = src[cur];
            cur = 1 - cur;
            var b = src[cur];
            b.Stop();
            b.clip = clip;
            b.loop = loop;
            b.volume = 0;
            if (clip != null) b.Play();
            if (fade != null) StopCoroutine(fade);
            fade = StartCoroutine(CrossFade(a, b));
        }

        IEnumerator CrossFade(AudioSource from, AudioSource to)
        {
            float start = from.volume;
            for (float t = 0; t < Fade; t += Time.unscaledDeltaTime)
            {
                float k = t / Fade;
                from.volume = start * (1 - k);
                to.volume = Volume * k;
                yield return null;
            }
            from.Stop();
            from.volume = 0;
            to.volume = Volume;
            fade = null;
        }
    }
}
