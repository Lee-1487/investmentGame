// 게임 효과음. 소리 파일 없이 코드로 짧은 음을 만들어 재생한다(에셋 추가·라이선스 걱정 없음).
// - Click: 모든 UiKit.Button을 누를 때
// - Buy / Sell: 내 보유 주식 수가 늘거나 줄었을 때(서버가 확정한 뒤, TabletView가 호출)
// - Coin: 코인을 써서 정보를 샀을 때
// - Turn: 새 단계(턴) 시작, 내 차례가 왔을 때
// 파형은 UiSoundSynth.cs에서 여러 겹(툭 + 질감 + 종소리 + 울림)으로 합성한다.
// 플레이 중이 아니거나(에디터 테스트) 배치 모드면 아무것도 하지 않는다.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlackWhale.UI.View
{
    public static class UiSound
    {
        public enum Kind { Click, Buy, Sell, Coin, Turn, Warning, ResultGood, ResultBad }

        /// <summary>전체 효과음 크기(0~1). 0이면 끈다.</summary>
        public static float Volume = 0.9f;

        private static AudioSource _source;
        private static readonly Dictionary<int, AudioClip> Clips = new Dictionary<int, AudioClip>();
        private static int _variant;
        private static float _lastClickTime = -1f;

        // 이 프로젝트는 'Enter Play Mode Options'로 도메인 리로드를 끄기 때문에 static 값이 이전 Play에서 그대로 남는다.
        // 이전 Play의 클릭 시각(예: 120초)이 남아 있으면 새 Play(0초부터)에서 클릭음이 계속 막히므로, Play 시작마다 초기화한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            _source = null;
            Clips.Clear();
            _lastClickTime = -1f;
        }

        public static void Play(Kind kind)
        {
            if (!Application.isPlaying || Application.isBatchMode || Volume <= 0f) return;
            try
            {
                // 같은 프레임에 버튼 소리가 여러 번 겹치지 않게
                if (kind == Kind.Click)
                {
                    float since = Time.unscaledTime - _lastClickTime;
                    if (since >= 0f && since < 0.04f) return;
                    _lastClickTime = Time.unscaledTime;
                }
                var source = Source();
                // 버튼·코인은 같은 소리를 살짝 다른 높이로 번갈아 내서 덜 기계적으로 들리게
                int variants = kind == Kind.Click ? 3 : kind == Kind.Coin ? 2 : 1;
                _variant = (_variant + 1) % 6;
                source.PlayOneShot(Clip(kind, _variant % variants), kind == Kind.Click ? Volume * 0.8f : Volume);
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
            _source.ignoreListenerPause = true;
            _source.ignoreListenerVolume = false;
            // 소리를 들을 귀(AudioListener)가 씬에 하나도 없으면 아무 소리도 안 난다 → 없으면 직접 붙인다.
            var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            bool hasActiveListener = false;
            foreach (var l in listeners) if (l.isActiveAndEnabled) { hasActiveListener = true; break; }
            if (!hasActiveListener) go.AddComponent<AudioListener>();
            if (AudioListener.volume <= 0f) AudioListener.volume = 1f;
            Debug.Log("UiSound: 효과음 준비 완료 (AudioListener " + (hasActiveListener ? "씬에 있음" : "없어서 새로 추가") + ")");
            return _source;
        }

        private static AudioClip Clip(Kind kind, int variant)
        {
            int key = (int)kind * 10 + variant;
            if (Clips.TryGetValue(key, out var clip) && clip != null) return clip;
            float[] samples = UiSoundSynth.Render(kind, variant); // 파형 합성은 UiSoundSynth.cs
            clip = AudioClip.Create("UiSound_" + kind + "_" + variant, samples.Length, 1, UiSoundSynth.SampleRate, false);
            clip.SetData(samples, 0);
            Clips[key] = clip;
            return clip;
        }
    }
}
