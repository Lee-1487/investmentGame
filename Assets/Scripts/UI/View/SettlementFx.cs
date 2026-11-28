// 정산·마감 연출(D). 값·진행은 서버 결과 그대로이고, 보이는 효과만 담당한다.
// - SettlementFx.Play: 턴 정산 결과가 오면 가운데 카드가 튀어나와 내 수익률·자산 변화를 세어 올리고,
//   수익이면 화면 가장자리가 금빛으로, 손실이면 붉게 번쩍이고 카드가 흔들린다. 약 3초 뒤 사라진다.
// - UrgencyEdges: 단계 마감 10초 전부터 화면 가장자리가 살짝 붉게 숨 쉬듯 물든다(시장 공개·정산·대기실·종료 제외).
using System.Collections.Generic;
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public static class SettlementFx
    {
        public const float Seconds = 3.2f;

        public static void Play(RectTransform screen, int turn, bool final, int rank, int turnReturnBps, long prevAssets, long nowAssets)
        {
            if (screen == null) return;
            var old = screen.Find("SettlementFx");
            if (old != null) Object.Destroy(old.gameObject);

            var root = UiKit.Rect(screen, "SettlementFx");
            UiKit.Stretch(root);
            bool gain = turnReturnBps > 0, loss = turnReturnBps < 0;
            Color tone = gain ? Palette.Up : loss ? Palette.Down : Palette.SubText;

            // 도트 + 글리치 띠: 위에 'N턴 정산 · 순위', 가운데 수익률을 크게 세어 올리고, 아래 자산 변화
            string head = final ? "최종 정산" : turn + "턴 정산";
            string rankText = rank > 0 ? (rank == 1 ? "1위!" : rank + "위") : "";
            var band = root.gameObject.AddComponent<GlitchBand>();
            band.Build(root, head + (rankText.Length > 0 ? "  ·  " + rankText : ""), "", Seconds, 210f);
            band.LayoutHead(18, 150, 24);

            var pct = UiKit.Label(band.Content, "", 52, tone, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(pct.rectTransform, 10, 50, 10, 54);
            UiAnim.Count(pct, 0, turnReturnBps, v => DisplayFormat.Percent((int)v), 1.2f, 0.3f);
            // 수익률 글자 뒤로 같은 색 도트 그림자(2칸 아래·오른쪽)
            var pctShadow = UiKit.Label(band.Content, "", 52, new Color(0, 0, 0, 0.55f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(pctShadow.rectTransform, 14, 50, 6, 58);
            pctShadow.transform.SetSiblingIndex(pct.transform.GetSiblingIndex());
            UiAnim.Count(pctShadow, 0, turnReturnBps, v => DisplayFormat.Percent((int)v), 1.2f, 0.3f);

            var assets = UiKit.Label(band.Content, "", 18, Palette.Text, TextAnchor.LowerCenter);
            UiKit.Stretch(assets.rectTransform, 10, 0, 10, 20);
            UiAnim.Count(assets, prevAssets, nowAssets, v => "자산 " + DisplayFormat.Money(prevAssets) + "  →  <b>" + DisplayFormat.Money(v) + "</b>", 1.4f, 0.4f);

            if (Application.isPlaying) Object.Destroy(root.gameObject, Seconds + 0.3f);
        }
    }

    /// <summary>delay초 뒤에 한 번 실행(연출 순서 맞추기용). 부모가 사라지면 함께 취소된다.</summary>
    public sealed class DelayedAction : MonoBehaviour
    {
        private float _left;
        private System.Action _action;

        public static void Run(RectTransform parent, float delay, System.Action action)
        {
            if (action == null) return;
            if (parent == null || !Application.isPlaying || delay <= 0f) { action(); return; }
            var go = new GameObject("DelayedAction");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<DelayedAction>();
            d._left = delay; d._action = action;
        }

        private void Update()
        {
            _left -= Time.unscaledDeltaTime;
            if (_left > 0f) return;
            var a = _action; _action = null;
            Destroy(gameObject);
            a?.Invoke();
        }
    }

    /// <summary>결과 발표: 정산 결과가 도착한 순간(차트 공개를 다 본 뒤) 화면 가운데 큰 제목이 튀어나왔다가 사라진다. 매 턴 1번.</summary>
    public static class ResultAnnouncement
    {
        public static void Play(RectTransform screen, int turn, bool final)
        {
            if (screen == null) return;
            var old = screen.Find("ResultAnnouncement");
            if (old != null) Object.Destroy(old.gameObject);
            var root = UiKit.Rect(screen, "ResultAnnouncement");
            UiKit.Stretch(root);

            // 도트 + 글리치 띠(화면 전환과 같은 연출)
            root.gameObject.AddComponent<GlitchBand>().Build(root, (final ? "최종" : turn + "턴") + " 결과 발표", "이번 턴 수익과 순위가 확정되었습니다", 2.4f);
            if (Application.isPlaying) Object.Destroy(root.gameObject, 2.6f);
        }
    }

    /// <summary>
    /// 화면 전환(턴·단계가 넘어갈 때): 도트 + 글리치 띠.
    /// 띠가 계단식(도트처럼 끊어지며)으로 펼쳐지고, 글자가 빨강·청록으로 갈라져 떨리다가 자리를 잡은 뒤,
    /// 띠가 조각나며 계단식으로 접혀 사라진다. 위아래에는 체크무늬 도트 테두리, 띠 위에는 주사선.
    /// </summary>
    public static class PhaseTransitionFx
    {
        public const float Seconds = 1.6f;

        public static void Play(RectTransform screen, string head, string sub)
        {
            if (screen == null) return;
            var old = screen.Find("PhaseTransitionFx");
            if (old != null) Object.Destroy(old.gameObject);
            var root = UiKit.Rect(screen, "PhaseTransitionFx");
            UiKit.Stretch(root);
            root.gameObject.AddComponent<GlitchBand>().Build(root, head, sub);
            if (Application.isPlaying) Object.Destroy(root.gameObject, Seconds + 0.2f);
        }
    }

    /// <summary>도트 글리치 띠 연출(PhaseTransitionFx 전용).</summary>
    public sealed class GlitchBand : MonoBehaviour
    {
        private const float Open = 0.2f, Glitch = 0.5f, Close = 0.3f;
        private float Height = 150f, CloseAt = 1.2f;
        private static Texture2D _dither, _scan;

        private RectTransform _band;
        private Text _main, _red, _cyan, _sub;
        private Vector2 _headBase; // 제목 기본 위치(떨림은 여기서 더함)
        private Image[] _slices;
        private CanvasGroup _group;
        private float _t, _nextJitter;
        private readonly System.Random _rng = new System.Random();

        private static Texture2D Tex(int w, int h, System.Func<int, int, Color32> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
            t.SetPixels32(px); t.Apply(); return t;
        }

        /// <summary>띠 안의 추가 내용(숫자 등)을 넣을 곳. 주사선 아래에 깔린다.</summary>
        public RectTransform Content { get; private set; }

        /// <summary>큰 제목(빨강·청록 잔상 포함)의 위치·크기 바꾸기.</summary>
        public void LayoutHead(float top, float bottom, int size)
        {
            foreach (var t in new[] { _red, _cyan, _main })
            {
                t.fontSize = size;
                t.rectTransform.offsetMin = new Vector2(0, bottom); t.rectTransform.offsetMax = new Vector2(0, -top);
            }
            _headBase = _main.rectTransform.anchoredPosition;
        }

        public void Build(RectTransform root, string head, string sub, float seconds = PhaseTransitionFx.Seconds, float height = 150f)
        {
            Height = height; CloseAt = seconds - 0.4f;
            if (_dither == null) _dither = Tex(2, 2, (x, y) => ((x + y) & 1) == 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0));
            if (_scan == null) _scan = Tex(1, 2, (x, y) => y == 0 ? new Color32(0, 0, 0, 90) : new Color32(0, 0, 0, 0));

            _band = UiKit.Rect(root, "Band");
            _band.anchorMin = new Vector2(0, .5f); _band.anchorMax = new Vector2(1, .5f);
            _band.sizeDelta = new Vector2(0, 0);
            _group = _band.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false; _group.interactable = false;
            _band.gameObject.AddComponent<RectMask2D>(); // 띠 높이 밖은 잘라서, 열리고 닫힐 때 글자가 띠 안에서만 보이게

            var bg = UiKit.Panel(_band, "Bg", new Color32(0x1A, 0x12, 0x0C, 0xF2)); UiKit.Stretch(bg.rectTransform);
            // 위·아래 체크무늬 도트 테두리(자주색)
            for (int i = 0; i < 2; i++)
            {
                var edge = UiKit.Rect(_band, i == 0 ? "TopDither" : "BottomDither");
                var raw = edge.gameObject.AddComponent<RawImage>();
                raw.texture = _dither; raw.color = Palette.MaroonBorder; raw.raycastTarget = false;
                edge.anchorMin = new Vector2(0, i == 0 ? 1 : 0); edge.anchorMax = new Vector2(1, i == 0 ? 1 : 0);
                edge.pivot = new Vector2(.5f, i == 0 ? 1 : 0); edge.sizeDelta = new Vector2(0, 8); edge.anchoredPosition = Vector2.zero;
                raw.uvRect = new UnityEngine.Rect(0, 0, 2048f / 4f, 2f); // 한 칸 = 4px
                var line = UiKit.Panel(_band, "Line", Palette.Accent);
                line.rectTransform.anchorMin = new Vector2(0, i == 0 ? 1 : 0); line.rectTransform.anchorMax = new Vector2(1, i == 0 ? 1 : 0);
                line.rectTransform.pivot = new Vector2(.5f, i == 0 ? 1 : 0); line.rectTransform.sizeDelta = new Vector2(0, 2);
                line.rectTransform.anchoredPosition = new Vector2(0, i == 0 ? -8 : 8);
            }
            // 글리치 조각(가로로 밀린 얇은 띠들)
            _slices = new Image[6];
            for (int i = 0; i < _slices.Length; i++)
            {
                var sl = UiKit.Panel(_band, "Slice" + i, i % 2 == 0 ? new Color32(0x4A, 0x16, 0x22, 0xE0) : new Color32(0x2E, 0x6E, 0x6E, 0xC0));
                sl.rectTransform.anchorMin = sl.rectTransform.anchorMax = new Vector2(.5f, .5f);
                sl.gameObject.SetActive(false);
                _slices[i] = sl;
            }
            // 글자: 빨강·청록 잔상 + 본 글자(크림)
            _red = Title(head, new Color32(0xFF, 0x3B, 0x5C, 0xC8));
            _cyan = Title(head, new Color32(0x3B, 0xE8, 0xFF, 0xC8));
            _main = Title(head, Palette.Text);
            _sub = UiKit.Label(_band, sub, 18, Palette.Accent, TextAnchor.LowerCenter);
            UiKit.Stretch(_sub.rectTransform, 0, 0, 0, 18);
            _headBase = _main.rectTransform.anchoredPosition;
            Content = UiKit.Rect(_band, "Content");
            UiKit.Stretch(Content);
            // 주사선
            var scan = UiKit.Rect(_band, "Scanlines");
            UiKit.Stretch(scan);
            var sr = scan.gameObject.AddComponent<RawImage>();
            sr.texture = _scan; sr.raycastTarget = false; sr.uvRect = new UnityEngine.Rect(0, 0, 1, Height / 4f); // 2px마다 한 줄
            Step(0f);
        }

        private Text Title(string head, Color color)
        {
            var t = UiKit.Label(_band, head, 54, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(t.rectTransform, 0, 0, 0, 34);
            return t;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            Step(_t);
        }

        private static float Steps(float v, int n) => Mathf.Floor(Mathf.Clamp01(v) * n) / n; // 계단식(도트처럼 끊어지게)

        private void Step(float t)
        {
            // 열기·닫기: 높이를 계단식으로
            float open = Steps(t / Open, 5);
            float close = t > CloseAt ? 1f - Steps((t - CloseAt) / Close, 5) : 1f;
            float h = Height * Mathf.Min(open, close);
            _band.sizeDelta = new Vector2(0, Mathf.Round(h / 4f) * 4f);
            _group.alpha = t > CloseAt + Close ? 0f : 1f;

            // 글리치: 처음 0.5초 + 사라지기 직전 잠깐 + 가운데 한 번 툭
            bool glitching = t < Glitch || (t > CloseAt - 0.08f && t < CloseAt + Close) || (t > 0.82f && t < 0.88f);
            if (t >= _nextJitter)
            {
                _nextJitter = t + 0.045f;
                float k = glitching ? 1f : 0f;
                _red.rectTransform.anchoredPosition = _headBase + new Vector2(Snap(-6f * k + Rand(4f) * k), Snap(Rand(3f) * k));
                _cyan.rectTransform.anchoredPosition = _headBase + new Vector2(Snap(6f * k + Rand(4f) * k), Snap(Rand(3f) * k));
                _main.rectTransform.anchoredPosition = _headBase + new Vector2(Snap(Rand(3f) * k), 0);
                _red.enabled = _cyan.enabled = glitching;
                for (int i = 0; i < _slices.Length; i++)
                {
                    bool on = glitching && _rng.NextDouble() < 0.6;
                    var sl = _slices[i];
                    sl.gameObject.SetActive(on);
                    if (!on) continue;
                    float w = 80f + (float)_rng.NextDouble() * 520f;
                    float hh = 4f * (1 + _rng.Next(4));
                    sl.rectTransform.sizeDelta = new Vector2(Snap(w), hh);
                    sl.rectTransform.anchoredPosition = new Vector2(Snap(Rand(420f)), Snap(Rand(Height * 0.45f)));
                }
            }
        }

        private float Rand(float r) => (float)(_rng.NextDouble() * 2 - 1) * r;
        private static float Snap(float v) => Mathf.Round(v / 2f) * 2f; // 2px 칸에 맞춤
    }

    /// <summary>역전 표시: 정산 결과 순위가 바뀌면 화면 위쪽에 '역전!' 띠가 내려와 누가 누구를 제쳤는지 알린다.</summary>
    public static class ReversalFx
    {
        public static void Play(RectTransform screen, string text, bool mine, float delay = 0.6f)
        {
            if (screen == null || string.IsNullOrEmpty(text)) return;
            var root = UiKit.Rect(screen, "ReversalFx");
            UiKit.Stretch(root);
            var band = UiKit.Panel(root, "Band", mine ? new Color32(0x28, 0x21, 0x18, 0xF0) : new Color32(0x18, 0x14, 0x0F, 0xF0), rounded: true);
            band.raycastTarget = false;
            UiKit.Place(band.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -90), new Vector2(560, 70));
            var label = UiKit.Label(band.transform, "<size=30><b>역전!</b></size>   " + text, 18, Color.white, TextAnchor.MiddleCenter);
            UiKit.Stretch(label.rectTransform, 10, 0, 10, 0);
            UiAnim.Rise(band.rectTransform, delay, -40f, 0.35f); // 위에서 내려오기
            UiAnim.FadeOut(band.rectTransform, delay + 3.0f, 0.4f);
            if (Application.isPlaying) Object.Destroy(root.gameObject, delay + 3.6f);
        }

        /// <summary>직전 순위와 새 순위를 비교해 '누가 누구를 제쳤는지' 문구를 만든다. 역전이 없으면 null.
        /// 가장 높은 순위에서 일어난 역전을 고른다.</summary>
        public static string Describe(IDictionary<string, int> before, IEnumerable<KeyValuePair<string, int>> after, System.Func<string, string> name, out bool involvesMe, string me)
        {
            involvesMe = false;
            if (before == null || before.Count == 0) return null;
            var now = new Dictionary<string, int>();
            foreach (var p in after) now[p.Key] = p.Value;
            string bestUp = null, bestPassed = null; int bestRank = int.MaxValue;
            foreach (var up in now)
            {
                if (!before.TryGetValue(up.Key, out int was) || up.Value >= was) continue;
                foreach (var down in now)
                {
                    if (down.Key == up.Key || !before.TryGetValue(down.Key, out int downWas)) continue;
                    // 전에는 down이 앞이었는데 이제 up이 앞
                    if (downWas < was && down.Value > up.Value && up.Value < bestRank)
                    { bestRank = up.Value; bestUp = up.Key; bestPassed = down.Key; }
                }
            }
            if (bestUp == null) return null;
            involvesMe = bestUp == me || bestPassed == me;
            return name(bestUp) + " → " + bestRank + "위 · " + name(bestPassed) + " 추월 (" + before[bestUp] + "위→" + bestRank + "위)";
        }
    }

    /// <summary>흔들림(손실 연출).</summary>
    public sealed class Shake : MonoBehaviour
    {
        private RectTransform _rt;
        private Vector2 _base;
        private float _t, _delay, _seconds, _power;

        public static void Play(RectTransform rt, float delay, float seconds, float power)
        {
            if (rt == null || !Application.isPlaying) return;
            var s = rt.gameObject.AddComponent<Shake>();
            s._rt = rt; s._delay = delay; s._seconds = seconds; s._power = power;
        }

        private void Update()
        {
            if (_rt == null) return;
            if (_delay > 0f) { _delay -= Time.unscaledDeltaTime; if (_delay <= 0f) _base = _rt.anchoredPosition; return; }
            _t += Time.unscaledDeltaTime;
            float k = 1f - _t / _seconds;
            if (k <= 0f) { _rt.anchoredPosition = _base; Destroy(this); return; }
            _rt.anchoredPosition = _base + new Vector2(Mathf.Sin(_t * 70f) * _power * k, 0);
        }
    }

    /// <summary>가장자리만 물드는 효과(가운데는 그대로). SetLevel(0~1)로 세기를 정하면 부드럽게 따라가며 숨 쉰다.</summary>
    public sealed class UrgencyEdges
    {
        private const int Layers = 6;
        private const float LayerWidth = 7f;
        private readonly List<Image> _images = new List<Image>();
        private readonly RectTransform _root;
        private readonly Color _color;
        private readonly float _maxAlpha, _breathSpeed;
        private float _level, _target;

        public UrgencyEdges(RectTransform screen) : this(screen, new Color(1f, 1f, 1f), 0.16f, 1.2f) { }

        /// <param name="breathSpeed">초당 깜박임 횟수 비슷한 값. 0이면 깜박이지 않음</param>
        public UrgencyEdges(RectTransform screen, Color color, float maxAlpha, float breathSpeed)
        {
            _color = color; _maxAlpha = maxAlpha; _breathSpeed = breathSpeed;
            _root = UiKit.Rect(screen, "UrgencyEdges");
            UiKit.Stretch(_root);
            for (int layer = 0; layer < Layers; layer++)
                foreach (bool horizontal in new[] { true, false })
                    foreach (int side in new[] { 0, 1 })
                    {
                        var img = UiKit.Panel(_root, "Edge" + layer, Color.clear);
                        img.raycastTarget = false;
                        var rt = img.rectTransform;
                        float inset = layer * LayerWidth;
                        rt.anchorMin = horizontal ? new Vector2(0, side) : new Vector2(side, 0);
                        rt.anchorMax = horizontal ? new Vector2(1, side) : new Vector2(side, 1);
                        rt.pivot = horizontal ? new Vector2(.5f, side) : new Vector2(side, .5f);
                        // 가로 띠는 좌우 모서리를 세로 띠와 겹치지 않게 안쪽으로
                        rt.sizeDelta = horizontal ? new Vector2(-2 * (inset + LayerWidth) , LayerWidth) : new Vector2(LayerWidth, -2 * inset);
                        rt.anchoredPosition = horizontal ? new Vector2(0, side == 0 ? inset : -inset) : new Vector2(side == 0 ? inset : -inset, 0);
                        _images.Add(img);
                    }
            _root.gameObject.SetActive(false);
        }

        public RectTransform Root => _root;

        /// <summary>0이면 꺼짐, 1이면 가장 진함.</summary>
        public void SetLevel(float level) => _target = Mathf.Clamp01(level);

        /// <summary>천천히 따라가지 않고 바로 이 세기로(번쩍임 연출용).</summary>
        public void SetLevelNow(float level) { _target = _level = Mathf.Clamp01(level); }

        public void Update()
        {
            _level = Mathf.MoveTowards(_level, _target, Time.unscaledDeltaTime * 1.5f);
            bool on = _level > 0.001f;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on) return;
            float breath = _breathSpeed <= 0f ? 1f : 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * Mathf.PI * _breathSpeed);
            for (int i = 0; i < _images.Count; i++)
            {
                int layer = i / 4;
                float falloff = 1f - layer / (float)Layers;            // 바깥이 진하고 안쪽으로 옅어짐
                _images[i].color = new Color(_color.r, _color.g, _color.b, _maxAlpha * falloff * falloff * _level * breath);
            }
        }
    }

    /// <summary>가장자리만 한 번 번쩍였다 사라지는 효과.</summary>
    public sealed class EdgeFlash : MonoBehaviour
    {
        private UrgencyEdges _edges;
        private float _t, _seconds;

        public static void Play(RectTransform parent, Color color, float seconds)
        {
            if (parent == null || !Application.isPlaying) return;
            var edges = new UrgencyEdges(parent, color, 0.45f, 0f);
            var driver = edges.Root.gameObject.AddComponent<EdgeFlash>();
            driver._edges = edges; driver._seconds = seconds;
            edges.SetLevelNow(1f);
            edges.Update();
        }

        private void Update()
        {
            if (_edges == null) return;
            _t += Time.unscaledDeltaTime;
            _edges.SetLevelNow(1f - _t / _seconds);
            _edges.Update();
            if (_t >= _seconds) Destroy(gameObject);
        }
    }
}
