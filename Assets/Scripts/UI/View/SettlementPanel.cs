// 정산 결과 상자(SettlementFx 전용). 화면 전환의 글리치 띠와 다른 연출:
// 가운데 도트 상자가 작은 조각 단위로 하나씩 드러나며(디졸브) 나타나고,
// 수익률을 크게 세어 올리는 동안 이익이면 금빛 도트가 솟고 손실이면 파란 도트가 떨어진다.
// 끝날 때는 조각이 다시 하나씩 덮이며 사라진다.
using UnityEngine;
using UnityEngine.UI;
using BlackWhale.UI.Presentation;

namespace BlackWhale.UI.View
{
    public sealed class SettlementPanel : MonoBehaviour
    {
        private const int Cols = 16, Rows = 7;
        private const float In = 0.45f, Out = 0.45f;
        private static float OutStart => SettlementFx.Seconds - 0.55f;

        private readonly System.Random _rng = new System.Random();
        private Image[] _blocks;
        private float[] _inAt, _outAt;
        private RectTransform _box, _particles;
        private bool _gain, _loss;
        private float _t, _nextParticle;
        private Text _star;

        public void Build(RectTransform root, string head, int rank, int bps, long prev, long now, Color tone)
        {
            _gain = bps > 0; _loss = bps < 0;
            const float w = 560f, h = 230f;

            // 상자: 두꺼운 황토 도트 테두리 + 어두운 갈색 바탕
            var frame = UiKit.Panel(root, "SettleBox", Palette.CardBorder, rounded: true);
            _box = frame.rectTransform;
            UiKit.Place(_box, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(w, h));
            var fill = UiKit.Panel(_box, "Fill", new Color32(0x1E, 0x15, 0x0E, 0xFA), rounded: true);
            UiKit.Stretch(fill.rectTransform, 4, 4, 4, 4);
            // 네 모서리 베이지 도트 장식
            for (int i = 0; i < 4; i++)
            {
                var c = UiKit.Panel(_box, "Corner" + i, Palette.Accent);
                c.rectTransform.anchorMin = c.rectTransform.anchorMax = new Vector2(i % 2, i / 2);
                c.rectTransform.sizeDelta = new Vector2(8, 8);
                c.rectTransform.anchoredPosition = new Vector2(i % 2 == 0 ? 6 : -6, i / 2 == 0 ? 6 : -6);
            }

            // 머리띠(자주색) + 제목
            var bar = UiKit.Panel(_box, "HeadBar", Palette.Maroon);
            bar.rectTransform.anchorMin = new Vector2(0, 1); bar.rectTransform.anchorMax = Vector2.one;
            bar.rectTransform.offsetMin = new Vector2(4, -48); bar.rectTransform.offsetMax = new Vector2(-4, -4);
            var title = UiKit.Label(bar.transform, "▶ " + head, 20, Palette.MaroonText, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Stretch(title.rectTransform, 16, 0, 120, 0);

            // 순위 배지(베이지 도트 상자), 1위면 깜박이는 별
            if (rank > 0)
            {
                var badge = UiKit.Panel(bar.transform, "RankBadge", Palette.Accent, rounded: true);
                badge.sprite = UiKit.PixelBevel; badge.pixelsPerUnitMultiplier = UiKit.PixelScale;
                badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(1, .5f);
                badge.rectTransform.sizeDelta = new Vector2(92, 32);
                badge.rectTransform.anchoredPosition = new Vector2(-58, 0);
                var label = UiKit.Label(badge.transform, rank + "위", 18, Palette.OnAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Stretch(label.rectTransform, 0, 0, 0, 2);
                if (rank == 1)
                {
                    _star = UiKit.Label(bar.transform, "★", 22, new Color32(0xFF, 0xD2, 0x4A, 0xFF), TextAnchor.MiddleCenter, FontStyle.Bold);
                    _star.rectTransform.anchorMin = _star.rectTransform.anchorMax = new Vector2(1, .5f);
                    _star.rectTransform.sizeDelta = new Vector2(30, 30);
                    _star.rectTransform.anchoredPosition = new Vector2(-118, 0);
                }
            }

            // 수익률(크게, 도트 그림자) + 구분선 + 자산 변화
            var shadow = UiKit.Label(_box, "", 58, new Color(0, 0, 0, .6f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(shadow.rectTransform, 4, 58, 0, 48); // 오른쪽 아래로 4px
            var pct = UiKit.Label(_box, "", 58, tone, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(pct.rectTransform, 0, 54, 4, 52);
            UiAnim.Count(pct, 0, bps, v => DisplayFormat.Percent((int)v), 1.2f, In);
            UiAnim.Count(shadow, 0, bps, v => DisplayFormat.Percent((int)v), 1.2f, In);
            var divider = UiKit.Panel(_box, "Divider", Palette.ChartFrame);
            divider.rectTransform.anchorMin = new Vector2(0, 0); divider.rectTransform.anchorMax = new Vector2(1, 0);
            divider.rectTransform.offsetMin = new Vector2(24, 50); divider.rectTransform.offsetMax = new Vector2(-24, 52);
            var assets = UiKit.Label(_box, "", 18, Palette.Text, TextAnchor.MiddleCenter);
            UiKit.Stretch(assets.rectTransform, 10, 180, 10, 4);
            UiAnim.Count(assets, prev, now, v => "자산  " + DisplayFormat.Money(prev) + "  ▶  <b>" + DisplayFormat.Money(v) + "</b>", 1.4f, In + 0.1f);

            // 입자 층(상자 주변을 날아다니는 도트)
            _particles = UiKit.Rect(root, "Particles");
            UiKit.Stretch(_particles);

            // 디졸브용 도트 덮개(상자 위 격자): 조각마다 사라지는/다시 덮이는 시각이 다름
            var cover = UiKit.Rect(_box, "Cover");
            UiKit.Stretch(cover);
            _blocks = new Image[Cols * Rows]; _inAt = new float[_blocks.Length]; _outAt = new float[_blocks.Length];
            for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Cols; x++)
            {
                int i = y * Cols + x;
                var b = UiKit.Panel(cover, "B" + i, Palette.Background);
                b.raycastTarget = false;
                b.rectTransform.anchorMin = new Vector2(x / (float)Cols, y / (float)Rows);
                b.rectTransform.anchorMax = new Vector2((x + 1) / (float)Cols, (y + 1) / (float)Rows);
                b.rectTransform.offsetMin = Vector2.zero; b.rectTransform.offsetMax = new Vector2(1, 1);
                _blocks[i] = b;
                _inAt[i] = (float)_rng.NextDouble() * In;
                _outAt[i] = OutStart + (float)_rng.NextDouble() * Out * 0.8f;
            }
            _nextParticle = In;
            Step();
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            Step();
        }

        private void Step()
        {
            // 덮개 조각: 나타날 때는 하나씩 걷히고, 끝날 때는 하나씩 다시 덮임
            for (int i = 0; i < _blocks.Length; i++)
            {
                bool covered = _t < _inAt[i] || _t >= _outAt[i];
                if (_blocks[i].enabled != covered) _blocks[i].enabled = covered;
            }
            if (_t >= OutStart + Out && _box.gameObject.activeSelf) _box.gameObject.SetActive(false);
            if (_star != null) _star.enabled = ((int)(_t * 4f) & 1) == 0;

            // 입자: 이익 → 금빛 도트가 상자 아래에서 솟음 / 손실 → 파란 도트가 상자에서 떨어짐
            if ((_gain || _loss) && _t > In && _t < OutStart && _t >= _nextParticle)
            {
                _nextParticle = _t + 0.06f;
                Spawn();
            }
        }

        private void Spawn()
        {
            var img = UiKit.Panel(_particles, "P", _gain ? new Color32(0xFF, 0xD2, 0x4A, 0xFF) : new Color32(0x3D, 0x8B, 0xFF, 0xFF));
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
            float size = _rng.NextDouble() < 0.5 ? 6f : 4f;
            rt.sizeDelta = new Vector2(size, size);
            float x = (float)(_rng.NextDouble() * 2 - 1) * 260f;
            rt.anchoredPosition = new Vector2(Mathf.Round(x / 2f) * 2f, _gain ? -110f : -100f);
            var vel = new Vector2((float)(_rng.NextDouble() * 2 - 1) * 40f, _gain ? 260f + (float)_rng.NextDouble() * 140f : -60f);
            img.gameObject.AddComponent<PixelParticle>().Init(vel, _gain ? -420f : -520f, 0.9f);
        }
    }

    /// <summary>작은 도트 하나: 2px 칸에 맞춰 움직이고 계단식으로 흐려지며 사라진다.</summary>
    public sealed class PixelParticle : MonoBehaviour
    {
        private Vector2 _vel, _pos;
        private float _gravity, _life, _t;
        private Image _img;

        public void Init(Vector2 velocity, float gravity, float life)
        {
            _vel = velocity; _gravity = gravity; _life = life;
            _img = GetComponent<Image>();
            _pos = ((RectTransform)transform).anchoredPosition;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _t += dt;
            if (_t >= _life) { Destroy(gameObject); return; }
            _vel.y += _gravity * dt;
            _pos += _vel * dt;
            ((RectTransform)transform).anchoredPosition = new Vector2(Mathf.Round(_pos.x / 2f) * 2f, Mathf.Round(_pos.y / 2f) * 2f);
            var c = _img.color; c.a = Mathf.Floor((1f - _t / _life) * 4f) / 4f; _img.color = c;
        }
    }
}
