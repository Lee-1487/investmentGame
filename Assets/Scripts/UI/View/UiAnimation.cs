// 화면 연출용 작은 애니메이션(순위 이동·숫자 증가·강조). 게임 진행·서버 시각과 무관한 표시 효과만 한다.
// - 모두 Time.unscaledDeltaTime 기준. 대상이 사라지면 함께 사라진다.
// - 애니메이션이 끝나면 항상 최종 값으로 맞춘다(중간 값이 남지 않음).
// - Play 중이 아니면(EditMode 테스트 등) 바로 최종 값으로 둔다.
using System;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public static class UiAnim
    {
        public const float SlideSeconds = 0.75f;
        public const float CountSeconds = 1.1f;

        /// <summary>위치를 from → to로 부드럽게(ease-out) 이동.</summary>
        public static void Slide(RectTransform rt, Vector2 from, Vector2 to, float seconds = SlideSeconds, float delay = 0f)
        {
            if (rt == null) return;
            if (!Application.isPlaying || seconds <= 0f || from == to) { rt.anchoredPosition = to; return; }
            var a = Get<SlideAnim>(rt.gameObject);
            a.Begin(rt, from, to, seconds, delay);
        }

        /// <summary>숫자를 from → to로 세면서 표시. format이 문구를 만든다.</summary>
        public static void Count(Text text, long from, long to, Func<long, string> format, float seconds = CountSeconds, float delay = 0f)
        {
            if (text == null || format == null) return;
            if (!Application.isPlaying || seconds <= 0f || from == to) { text.text = format(to); return; }
            var a = Get<CountAnim>(text.gameObject);
            a.Begin(text, from, to, format, seconds, delay);
        }

        /// <summary>잠깐 커졌다 돌아오는 강조. flash가 있으면 그 이미지 색도 잠깐 바꾼다.</summary>
        public static void Pulse(RectTransform rt, float delay = 0f, float scale = 1.08f, Image flash = null, Color flashColor = default)
        {
            if (rt == null || !Application.isPlaying) return;
            var a = Get<PulseAnim>(rt.gameObject);
            a.Begin(rt, delay, scale, flash, flashColor);
        }

        /// <summary>투명 → 보임 + 아래에서 살짝 올라오며 등장.</summary>
        public static void Rise(RectTransform rt, float delay = 0f, float distance = 40f, float seconds = 0.55f)
        {
            if (rt == null) return;
            var to = rt.anchoredPosition;
            var group = Get<CanvasGroup>(rt.gameObject); // ??는 Unity 가짜 null에 안전하지 않아 쓰지 않음
            if (!Application.isPlaying) { group.alpha = 1f; return; }
            group.alpha = 0f;
            Get<FadeAnim>(rt.gameObject).Begin(group, seconds, delay);
            Slide(rt, to - new Vector2(0, distance), to, seconds, delay);
        }

        /// <summary>0~1 진행도를 ease-out(빠르게 출발, 부드럽게 멈춤)으로.</summary>
        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            float u = 1f - t;
            return 1f - u * u * u;
        }

        private static T Get<T>(GameObject go) where T : Component
            => go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();
    }

    public sealed class SlideAnim : MonoBehaviour
    {
        private RectTransform _rt;
        private Vector2 _from, _to;
        private float _t, _seconds, _delay;
        private bool _running;

        public void Begin(RectTransform rt, Vector2 from, Vector2 to, float seconds, float delay)
        {
            _rt = rt; _from = from; _to = to; _seconds = seconds; _delay = delay; _t = 0f; _running = true;
            _rt.anchoredPosition = from;
        }

        private void Update()
        {
            if (!_running || _rt == null) return;
            if (_delay > 0f) { _delay -= Time.unscaledDeltaTime; return; }
            _t += Time.unscaledDeltaTime / _seconds;
            _rt.anchoredPosition = Vector2.LerpUnclamped(_from, _to, UiAnim.EaseOut(_t));
            if (_t >= 1f) { _rt.anchoredPosition = _to; _running = false; }
        }
    }

    public sealed class CountAnim : MonoBehaviour
    {
        private Text _text;
        private long _from, _to;
        private Func<long, string> _format;
        private float _t, _seconds, _delay;
        private bool _running;

        public void Begin(Text text, long from, long to, Func<long, string> format, float seconds, float delay)
        {
            _text = text; _from = from; _to = to; _format = format; _seconds = seconds; _delay = delay; _t = 0f; _running = true;
            _text.text = format(from);
        }

        private void Update()
        {
            if (!_running || _text == null) return;
            if (_delay > 0f) { _delay -= Time.unscaledDeltaTime; return; }
            _t += Time.unscaledDeltaTime / _seconds;
            double e = UiAnim.EaseOut(_t);
            long v = _from + (long)Math.Round((_to - _from) * e);
            _text.text = _format(_t >= 1f ? _to : v);
            if (_t >= 1f) _running = false;
        }
    }

    public sealed class PulseAnim : MonoBehaviour
    {
        private RectTransform _rt;
        private Image _flash;
        private Color _flashColor, _baseColor;
        private float _t, _delay, _scale;
        private bool _running;
        private const float Seconds = 0.6f;

        public void Begin(RectTransform rt, float delay, float scale, Image flash, Color flashColor)
        {
            if (_running) Finish();
            _rt = rt; _delay = delay; _scale = scale; _flash = flash; _flashColor = flashColor; _t = 0f; _running = true;
            if (_flash != null) _baseColor = _flash.color;
        }

        private void Update()
        {
            if (!_running || _rt == null) return;
            if (_delay > 0f) { _delay -= Time.unscaledDeltaTime; return; }
            _t += Time.unscaledDeltaTime / Seconds;
            float wave = Mathf.Sin(Mathf.Clamp01(_t) * Mathf.PI); // 0 → 1 → 0
            _rt.localScale = Vector3.one * (1f + (_scale - 1f) * wave);
            if (_flash != null) _flash.color = Color.Lerp(_baseColor, _flashColor, wave);
            if (_t >= 1f) Finish();
        }

        private void Finish()
        {
            if (_rt != null) _rt.localScale = Vector3.one;
            if (_flash != null) _flash.color = _baseColor;
            _running = false;
        }
    }

    public sealed class FadeAnim : MonoBehaviour
    {
        private CanvasGroup _group;
        private float _t, _seconds, _delay;
        private bool _running;

        public void Begin(CanvasGroup group, float seconds, float delay)
        {
            _group = group; _seconds = seconds; _delay = delay; _t = 0f; _running = true;
        }

        private void Update()
        {
            if (!_running || _group == null) return;
            if (_delay > 0f) { _delay -= Time.unscaledDeltaTime; return; }
            _t += Time.unscaledDeltaTime / _seconds;
            _group.alpha = Mathf.Clamp01(_t);
            if (_t >= 1f) { _group.alpha = 1f; _running = false; }
        }
    }
}
