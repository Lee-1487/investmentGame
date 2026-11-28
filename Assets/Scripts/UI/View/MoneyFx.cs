// 실시간 매매 중 돈이 늘거나 줄 때의 화면 효과(D, 보이는 효과만).
// - Gain: 도트 초록 화살표 몇 개가 화면 아래쪽에서 위로 칸칸이 올라가며 사라진다.
// - Loss: 화면 가장자리가 도트 계단 모양으로 붉게 번쩍이고, 화면이 짧게 흔들린다.
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public static class MoneyFx
    {
        private static Sprite _arrow;
        private static Texture2D _vignette;

        private static Sprite Arrow
        {
            get
            {
                if (_arrow != null) return _arrow;
                string[] art =
                {
                    "....#....",
                    "...###...",
                    "..#####..",
                    ".#######.",
                    "#########",
                    "...###...",
                    "...###...",
                    "...###...",
                    "...###...",
                    "...###...",
                };
                int w = art[0].Length, h = art.Length;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    px[y * w + x] = art[h - 1 - y][x] == '#' ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
                tex.SetPixels32(px); tex.Apply();
                _arrow = Sprite.Create(tex, new UnityEngine.Rect(0, 0, w, h), new Vector2(.5f, .5f), 100f);
                _arrow.hideFlags = HideFlags.DontSave;
                return _arrow;
            }
        }

        // 가장자리일수록 진한 빨강(4단계 계단, 도트 느낌)
        private static Texture2D Vignette
        {
            get
            {
                if (_vignette != null) return _vignette;
                const int w = 192, h = 108; // 한 칸이 작아져 빨간 테두리가 얇아짐
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y)); // 가장자리까지 칸 수
                    byte a = d == 0 ? (byte)170 : d == 1 ? (byte)120 : d == 2 ? (byte)75 : d == 3 ? (byte)35 : (byte)0;
                    if (d == 3 && ((x + y) & 1) == 1) a = 0; // 마지막 단계는 체크무늬로 흐리게
                    px[y * w + x] = new Color32(0xFF, 0x2A, 0x3A, a);
                }
                tex.SetPixels32(px); tex.Apply();
                _vignette = tex;
                return tex;
            }
        }

        /// <summary>돈을 얻었을 때: 초록 도트 화살표가 위로.</summary>
        public static void Gain(RectTransform fx)
        {
            if (fx == null) return;
            var root = UiKit.Rect(fx, "MoneyGainFx");
            UiKit.Stretch(root);
            root.gameObject.AddComponent<GainArrows>().Build(root, Arrow);
            if (Application.isPlaying) Object.Destroy(root.gameObject, GainArrows.Seconds + 0.2f);
        }

        /// <summary>돈을 잃었을 때: 가장자리 빨간 번쩍임 + 화면 흔들림.</summary>
        public static void Loss(RectTransform fx, RectTransform shakeTarget)
        {
            if (fx == null) return;
            var old = fx.Find("MoneyLossFx");
            if (old != null) Object.Destroy(old.gameObject);
            var root = UiKit.Rect(fx, "MoneyLossFx");
            UiKit.Stretch(root);
            var raw = root.gameObject.AddComponent<RawImage>();
            raw.texture = Vignette; raw.raycastTarget = false;
            root.gameObject.AddComponent<LossFlash>().Init(raw, shakeTarget);
            if (Application.isPlaying) Object.Destroy(root.gameObject, LossFlash.Seconds + 0.1f);
        }

        private sealed class GainArrows : MonoBehaviour
        {
            public const float Seconds = 1.1f;
            private const float MaxAlpha = 0.55f; // 더 투명하게
            private RectTransform[] _arrows;
            private Image[] _images;
            private float[] _delay;
            private Vector2[] _start;
            private float _t;

            public void Build(RectTransform root, Sprite sprite)
            {
                const int count = 5;
                _arrows = new RectTransform[count]; _images = new Image[count]; _delay = new float[count]; _start = new Vector2[count];
                var rng = new System.Random();
                for (int i = 0; i < count; i++)
                {
                    var img = UiKit.Panel(root, "Arrow" + i, new Color(0.36f, 0.82f, 0.48f, MaxAlpha)); // 반투명 초록
                    img.sprite = sprite; img.raycastTarget = false;
                    var rt = img.rectTransform;
                    // 나올 때마다 화면 전체에 무작위로: 가로는 화면을 5칸으로 나눠 칸마다 1개(골고루), 칸 안 위치·높이는 무작위
                    float cell = 1f / count;
                    float x = cell * i + cell * (0.15f + 0.7f * (float)rng.NextDouble());
                    float y = 0.1f + 0.55f * (float)rng.NextDouble();
                    rt.anchorMin = rt.anchorMax = new Vector2(x, y);
                    float size = rng.NextDouble() < 0.5 ? 7f : 6f;          // 도트 한 칸 크기(px): 크게
                    rt.sizeDelta = new Vector2(9 * size, 10 * size);
                    _start[i] = Vector2.zero;
                    _delay[i] = i * 0.08f + (float)rng.NextDouble() * 0.06f;
                    _arrows[i] = rt; _images[i] = img;
                }
                Step();
            }

            private void Update() { _t += Time.unscaledDeltaTime; Step(); }

            private void Step()
            {
                for (int i = 0; i < _arrows.Length; i++)
                {
                    float p = Mathf.Clamp01((_t - _delay[i]) / (Seconds - 0.3f));
                    float rise = Mathf.Round(p * 220f / 6f) * 6f;              // 6px 칸씩 올라감
                    _arrows[i].anchoredPosition = _start[i] + new Vector2(0, rise);
                    var c = _images[i].color;
                    c.a = MaxAlpha * (p <= 0f ? 0f : p < 0.7f ? 1f : Mathf.Floor((1f - (p - 0.7f) / 0.3f) * 4f) / 4f); // 끝에 계단식으로 흐려짐
                    _images[i].color = c;
                }
            }
        }

        private sealed class LossFlash : MonoBehaviour
        {
            public const float Seconds = 0.6f;
            private const float ShakeSeconds = 0.35f, ShakePx = 10f, ShakeStep = 0.08f; // 흔들림 위치를 0.08초마다만 바꿈(덜 정신없게)
            private RawImage _raw;
            private RectTransform _shake;
            private Vector2 _home;
            private float _t, _nextShake;
            private readonly System.Random _rng = new System.Random();

            public void Init(RawImage raw, RectTransform shake)
            {
                _raw = raw; _shake = shake;
                if (_shake != null)
                {
                    if (!_shake.TryGetComponent<ShakeHome>(out var holder)) holder = _shake.gameObject.AddComponent<ShakeHome>();
                    if (!holder.Shaking) { holder.Home = _shake.anchoredPosition; }
                    holder.Shaking = true;
                    _home = holder.Home;
                }
                Step();
            }

            private void Update() { _t += Time.unscaledDeltaTime; Step(); }

            private void Step()
            {
                float p = Mathf.Clamp01(_t / Seconds);
                float a = p < 0.15f ? p / 0.15f : 1f - (p - 0.15f) / 0.85f;
                _raw.color = new Color(1, 1, 1, Mathf.Floor(a * 5f) / 5f);    // 계단식 번쩍임
                if (_shake == null) return;
                if (_t < ShakeSeconds)
                {
                    if (_t >= _nextShake)
                    {
                        _nextShake = _t + ShakeStep;
                        float k = ShakePx * (1f - _t / ShakeSeconds);
                        _shake.anchoredPosition = _home + new Vector2(Mathf.Round((float)(_rng.NextDouble() * 2 - 1) * k / 2f) * 2f,
                                                                      Mathf.Round((float)(_rng.NextDouble() * 2 - 1) * k / 2f) * 2f);
                    }
                }
                else Restore();
            }

            private void OnDestroy() => Restore();

            private void Restore()
            {
                if (_shake == null) return;
                _shake.anchoredPosition = _home;
                if (_shake.TryGetComponent<ShakeHome>(out var holder)) holder.Shaking = false;
            }
        }

        /// <summary>흔들기 전 원래 위치(여러 번 겹쳐 흔들려도 원래 자리로 돌아오게).</summary>
        private sealed class ShakeHome : MonoBehaviour { public Vector2 Home; public bool Shaking; }
    }
}
