// 정산·마감 연출(D). 값·진행은 서버 결과 그대로이고, 보이는 효과만 담당한다.
// - SettlementFx.Play: 턴 정산 결과가 오면 가운데 카드가 튀어나와 내 수익률·자산 변화를 세어 올리고,
//   수익이면 색종이가 터지고(1위면 더 많이), 손실이면 카드가 흔들리며 붉게 번쩍인다. 약 3초 뒤 사라진다.
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

            // 화면 번쩍임(수익: 금빛, 손실: 붉은빛)
            var flash = UiKit.Panel(root, "Flash", gain ? new Color(1f, 0.85f, 0.3f, 0.28f) : loss ? new Color(0.9f, 0.1f, 0.1f, 0.22f) : new Color(1, 1, 1, 0.15f));
            UiKit.Stretch(flash.rectTransform);
            flash.raycastTarget = false;
            UiAnim.FadeOut(flash.rectTransform, 0.05f, 0.6f);

            // 가운데 카드
            var card = UiKit.Panel(root, "Card", ScreenKit.Ink, rounded: true);
            UiKit.Place(card.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(430, 200));
            card.raycastTarget = false;
            var border = card.gameObject.AddComponent<Outline>();
            border.effectColor = gain ? ScreenKit.Amber : tone; border.effectDistance = new Vector2(3, -3);

            string head = final ? "최종 정산" : turn + "턴 정산";
            string rankText = rank > 0 ? (rank == 1 ? "<color=#FBBF24><b>1위!</b></color>" : rank + "위") : "";
            var title = UiKit.Label(card.transform, "<b>" + head + "</b>   <size=16>" + rankText + "</size>", 20, Color.white, TextAnchor.UpperCenter);
            UiKit.Stretch(title.rectTransform, 10, 16, 10, 0);

            var pct = UiKit.Label(card.transform, "", 46, tone, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(pct.rectTransform, 10, 50, 10, 60);
            UiAnim.Count(pct, 0, turnReturnBps, v => DisplayFormat.Percent((int)v), 1.2f, 0.25f);

            var assets = UiKit.Label(card.transform, "", 18, new Color(0.9f, 0.92f, 0.95f), TextAnchor.LowerCenter);
            UiKit.Stretch(assets.rectTransform, 10, 0, 10, 18);
            UiAnim.Count(assets, prevAssets, nowAssets, v => "자산 " + DisplayFormat.Money(prevAssets) + "  →  <b>" + DisplayFormat.Money(v) + "</b>", 1.4f, 0.35f);

            UiAnim.Rise(card.rectTransform, 0f, 50f, 0.35f);
            UiAnim.Pulse(card.rectTransform, 0.35f, gain ? 1.12f : 1.05f);
            if (loss) Shake.Play(card.rectTransform, 0.4f, 0.45f, 9f);

            if (gain)
            {
                int pieces = rank == 1 || final ? 90 : 50;
                ConfettiBurst.Spawn(root, pieces, new Vector2(0, 20));
            }

            UiAnim.FadeOut(card.rectTransform, Seconds - 0.5f, 0.5f);
            if (Application.isPlaying) Object.Destroy(root.gameObject, Seconds + 0.3f);
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

    /// <summary>색종이: 가운데에서 위로 터졌다가 중력으로 떨어지며 사라진다.</summary>
    public sealed class ConfettiBurst : MonoBehaviour
    {
        private static readonly Color32[] Colors =
        {
            new Color32(0xEF, 0x44, 0x44, 0xFF), new Color32(0xF5, 0x9E, 0x0B, 0xFF), new Color32(0xFB, 0xBF, 0x24, 0xFF),
            new Color32(0x10, 0xB9, 0x81, 0xFF), new Color32(0x25, 0x63, 0xEB, 0xFF), new Color32(0x7C, 0x3A, 0xED, 0xFF)
        };

        private readonly List<RectTransform> _pieces = new List<RectTransform>();
        private readonly List<Vector2> _velocity = new List<Vector2>();
        private readonly List<float> _spin = new List<float>();
        private float _age;
        private const float Life = 2.6f;

        public static void Spawn(RectTransform parent, int count, Vector2 origin)
        {
            if (parent == null || !Application.isPlaying) return;
            var holder = UiKit.Rect(parent, "Confetti");
            UiKit.Stretch(holder);
            var burst = holder.gameObject.AddComponent<ConfettiBurst>();
            var group = holder.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            for (int i = 0; i < count; i++)
            {
                var img = UiKit.Panel(holder, "Piece", Colors[i % Colors.Length]);
                img.raycastTarget = false;
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.sizeDelta = new Vector2(Random.Range(6f, 11f), Random.Range(10f, 16f));
                rt.anchoredPosition = origin + Random.insideUnitCircle * 20f;
                rt.localRotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
                float angle = Random.Range(20f, 160f) * Mathf.Deg2Rad;
                float speed = Random.Range(380f, 820f);
                burst._pieces.Add(rt);
                burst._velocity.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed);
                burst._spin.Add(Random.Range(-540f, 540f));
            }
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _age += dt;
            for (int i = 0; i < _pieces.Count; i++)
            {
                if (_pieces[i] == null) continue;
                var v = _velocity[i];
                v.y -= 1100f * dt;      // 중력
                v *= 1f - 0.9f * dt;    // 공기 저항
                _velocity[i] = v;
                _pieces[i].anchoredPosition += v * dt;
                _pieces[i].Rotate(0, 0, _spin[i] * dt);
            }
            var group = GetComponent<CanvasGroup>();
            if (group != null) group.alpha = Mathf.Clamp01((Life - _age) / 0.6f);
            if (_age >= Life) Destroy(gameObject);
        }
    }

    /// <summary>화면 가장자리 붉은 기운. SetLevel(0~1)로 세기를 정하면 부드럽게 따라가며 천천히 숨 쉰다.</summary>
    public sealed class UrgencyEdges
    {
        private const int Layers = 6;
        private const float LayerWidth = 7f;
        private readonly List<Image> _images = new List<Image>();
        private readonly RectTransform _root;
        private float _level, _target;

        public UrgencyEdges(RectTransform screen)
        {
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

        public void Update()
        {
            _level = Mathf.MoveTowards(_level, _target, Time.unscaledDeltaTime * 1.5f);
            bool on = _level > 0.001f;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on) return;
            float breath = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 1.2f);
            for (int i = 0; i < _images.Count; i++)
            {
                int layer = i / 4;
                float falloff = 1f - layer / (float)Layers;            // 바깥이 진하고 안쪽으로 옅어짐
                _images[i].color = new Color(0.86f, 0.12f, 0.12f, 0.16f * falloff * falloff * _level * breath);
            }
        }
    }
}
