// 게임 효과음. 소리 파일 없이 코드로 짧은 음을 만들어 재생한다(에셋 추가·라이선스 걱정 없음).
// - Click: 모든 UiKit.Button을 누를 때
// - Buy / Sell: 내 보유 주식 수가 늘거나 줄었을 때(서버가 확정한 뒤, TabletView가 호출)
// - Coin: 코인을 써서 정보를 샀을 때
// 플레이 중이 아니거나(에디터 테스트) 배치 모드면 아무것도 하지 않는다.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlackWhale.UI.View
{
    public static class UiSound
    {
        public enum Kind { Click, Buy, Sell, Coin }

        /// <summary>전체 효과음 크기(0~1). 0이면 끈다.</summary>
        public static float Volume = 0.5f;

        private const int SampleRate = 44100;
        private static AudioSource _source;
        private static readonly Dictionary<Kind, AudioClip> Clips = new Dictionary<Kind, AudioClip>();
        private static float _lastClickTime = -1f;

        public static void Play(Kind kind)
        {
            if (!Application.isPlaying || Application.isBatchMode || Volume <= 0f) return;
            try
            {
                // 같은 프레임에 버튼 소리가 여러 번 겹치지 않게
                if (kind == Kind.Click)
                {
                    if (Time.unscaledTime - _lastClickTime < 0.04f) return;
                    _lastClickTime = Time.unscaledTime;
                }
                var source = Source();
                source.PlayOneShot(Clip(kind), Volume);
            }
            catch (Exception e)
            {
                Debug.LogWarning("UiSound: " + e.Message); // 소리 문제로 게임이 멈추면 안 된다
            }
        }

        private static AudioSource Source()
        {
            if (_source != null) return _source;
            var go = new GameObject("UiSound");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // 2D 소리
            return _source;
        }

        private static AudioClip Clip(Kind kind)
        {
            if (Clips.TryGetValue(kind, out var clip) && clip != null) return clip;
            float[] samples;
            switch (kind)
            {
                case Kind.Buy: samples = Notes(new[] { 660f, 990f }, 0.07f, 0.35f); break;   // 올라가는 두 음
                case Kind.Sell: samples = Notes(new[] { 880f, 587f }, 0.07f, 0.35f); break;  // 내려가는 두 음
                case Kind.Coin: samples = Notes(new[] { 1319f, 1976f }, 0.06f, 0.30f); break; // 짤랑(높은 두 음)
                default: samples = Notes(new[] { 1200f }, 0.035f, 0.25f); break;             // 짧은 '틱'
            }
            clip = AudioClip.Create("UiSound_" + kind, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            Clips[kind] = clip;
            return clip;
        }

        /// <summary>주어진 음(Hz)들을 차례로 이어 붙인 짧은 소리. 각 음은 빠르게 커졌다 서서히 줄어든다.</summary>
        private static float[] Notes(float[] frequencies, float noteSeconds, float amplitude)
        {
            int perNote = Mathf.Max(1, (int)(SampleRate * noteSeconds));
            int tail = (int)(SampleRate * 0.03f); // 마지막 음 여운
            var samples = new float[perNote * frequencies.Length + tail];
            for (int n = 0; n < frequencies.Length; n++)
            {
                int length = perNote + (n == frequencies.Length - 1 ? tail : 0);
                for (int i = 0; i < length; i++)
                {
                    float t = (float)i / SampleRate;
                    float attack = Mathf.Clamp01(i / (SampleRate * 0.004f));
                    float decay = Mathf.Exp(-t * 28f);
                    // 기본음 + 약한 배음(조금 더 '띵' 소리)
                    float wave = Mathf.Sin(2f * Mathf.PI * frequencies[n] * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * frequencies[n] * t);
                    samples[n * perNote + i] += wave * attack * decay * amplitude;
                }
            }
            return samples;
        }
    }
}
