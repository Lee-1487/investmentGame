// 도트 테마 배경 효과: 어두운 갈색 바탕 위에 색 점(디더링) 띠가 흐르고 깜빡이는 '글리치' 배경.
// 코드로 저해상도 텍스처를 그려 점(Point) 필터로 늘려 보여 준다(한 칸 = 화면 2px).
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    [RequireComponent(typeof(RawImage))]
    public sealed class PixelBackdrop : MonoBehaviour
    {
        /// <summary>parent 전체를 덮는 배경 효과를 만든다(맨 뒤에 깔림).</summary>
        public static PixelBackdrop Create(Transform parent, string name = "PixelBackdrop")
        {
            var rt = UiKit.Rect(parent, name);
            UiKit.Stretch(rt);
            rt.SetAsFirstSibling();
            var raw = rt.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = Color.white;
            return rt.gameObject.AddComponent<PixelBackdrop>();
        }

        private const float TexelSize = 2f;     // 화면(캔버스) 2px = 텍스처 1칸
        private const float FrameSeconds = 0.11f; // 약 9fps로 갱신(레트로 느낌)

        private static readonly Color32 Base = new Color32(0x15, 0x10, 0x0B, 0xFF);
        private static readonly Color32[] BandColors =
        {
            new Color32(0x8A, 0x2E, 0x44, 0xFF), // 자주·빨강
            new Color32(0x3E, 0x8A, 0x4E, 0xFF), // 초록
            new Color32(0x45, 0x55, 0xB0, 0xFF), // 파랑
            new Color32(0x6A, 0x45, 0xA0, 0xFF), // 보라
            new Color32(0x3E, 0x86, 0x86, 0xFF), // 청록
        };

        private struct Band
        {
            public float X, Speed;
            public int Y, W, H, Color, Pattern;
            public bool Visible;
        }

        private RawImage _raw;
        private Texture2D _tex;
        private Color32[] _base, _px;
        private int _w, _h;
        private Band[] _bands;
        private Vector2Int[] _dots;
        private float _next;
        private System.Random _rng = new System.Random(20261127);

        private void Awake() => _raw = GetComponent<RawImage>();

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
        }

        private void OnRectTransformDimensionsChange() => _w = 0; // 크기가 바뀌면 다시 만든다

        private void Update()
        {
            var r = ((RectTransform)transform).rect;
            int w = Mathf.Clamp(Mathf.RoundToInt(r.width / TexelSize), 16, 1024);
            int h = Mathf.Clamp(Mathf.RoundToInt(r.height / TexelSize), 16, 640);
            if (w != _w || h != _h) Rebuild(w, h);
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + FrameSeconds;
            Step();
            Draw();
        }

        private void Rebuild(int w, int h)
        {
            _w = w; _h = h;
            if (_tex != null) Destroy(_tex);
            _tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            _raw.texture = _tex;
            _base = new Color32[w * h];
            _px = new Color32[w * h];

            // 바탕: 아주 어두운 갈색 + 옅은 점 잡음 + 가로 주사선 + 가장자리 어둡게
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = 1f;
                if ((y & 1) == 0) v *= 0.86f;                        // 주사선
                if (_rng.NextDouble() < 0.05) v *= 1.45f;            // 잡음 점
                float ex = Mathf.Min(x, w - 1 - x) / (w * 0.18f);
                float ey = Mathf.Min(y, h - 1 - y) / (h * 0.18f);
                v *= Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(Mathf.Min(ex, ey))); // 비네트
                _base[y * w + x] = new Color32((byte)Mathf.Min(255, Base.r * v), (byte)Mathf.Min(255, Base.g * v), (byte)Mathf.Min(255, Base.b * v), 255);
            }

            int count = Mathf.Clamp(w * h / 4200, 24, 72); // 레퍼런스처럼 띠를 촘촘하게
            _bands = new Band[count];
            for (int i = 0; i < count; i++) _bands[i] = NewBand(true);
            _dots = new Vector2Int[Mathf.Clamp(w * h / 6000, 10, 60)];
            for (int i = 0; i < _dots.Length; i++) _dots[i] = new Vector2Int(_rng.Next(w), _rng.Next(h));
            _next = 0;
        }

        private Band NewBand(bool anywhere)
        {
            var b = new Band
            {
                W = 14 + (int)(Mathf.Pow((float)_rng.NextDouble(), 1.4f) * _w * 0.36f),
                H = _rng.NextDouble() < 0.25 ? 8 + _rng.Next(14) : 2 + _rng.Next(8), // 가끔 두꺼운 덩어리
                Y = _rng.Next(_h),
                Color = _rng.Next(BandColors.Length),
                Pattern = _rng.Next(3),
                Speed = (float)(_rng.NextDouble() * 2.4 - 1.2),
                Visible = true,
            };
            b.X = anywhere ? _rng.Next(_w) - b.W / 2f : (b.Speed > 0 ? -b.W : _w);
            return b;
        }

        private void Step()
        {
            for (int i = 0; i < _bands.Length; i++)
            {
                var b = _bands[i];
                b.X += b.Speed;
                double roll = _rng.NextDouble();
                if (roll < 0.015) { b.X += _rng.Next(-24, 25); b.Y = Mathf.Clamp(b.Y + _rng.Next(-6, 7), 0, _h - 1); } // 글리치 점프
                b.Visible = _rng.NextDouble() > 0.06;                                                                       // 깜빡임
                if (b.X > _w + 4 || b.X + b.W < -4 || roll > 0.996) b = NewBand(false);
                _bands[i] = b;
            }
            if (_rng.NextDouble() < 0.3) { int d = _rng.Next(_dots.Length); _dots[d] = new Vector2Int(_rng.Next(_w), _rng.Next(_h)); }
        }

        private void Draw()
        {
            System.Array.Copy(_base, _px, _px.Length);
            foreach (var b in _bands)
            {
                if (!b.Visible) continue;
                var c = BandColors[b.Color];
                int x0 = Mathf.Max(0, Mathf.FloorToInt(b.X)), x1 = Mathf.Min(_w, Mathf.FloorToInt(b.X) + b.W);
                int y0 = Mathf.Max(0, b.Y), y1 = Mathf.Min(_h, b.Y + b.H);
                for (int y = y0; y < y1; y++)
                {
                    int row = y * _w;
                    for (int x = x0; x < x1; x++)
                    {
                        bool on;
                        switch (b.Pattern)
                        {
                            case 0: on = ((x + y) & 1) == 0; break;           // 체크무늬 디더
                            case 1: on = (y & 1) == 0 && (x % 3) != 2; break; // 가로 점선
                            default: on = ((x + y) & 1) == 0 || ((x ^ y) & 3) == 0; break; // 촘촘한 디더
                        }
                        if (!on) continue;
                        var o = _px[row + x];
                        _px[row + x] = new Color32((byte)((o.r + c.r * 3) >> 2), (byte)((o.g + c.g * 3) >> 2), (byte)((o.b + c.b * 3) >> 2), 255);
                    }
                }
            }
            foreach (var d in _dots)
                if (_rng.NextDouble() > 0.25) _px[d.y * _w + d.x] = new Color32(0xE8, 0xDC, 0xC0, 0xFF); // 반짝이는 점
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }
    }
}
