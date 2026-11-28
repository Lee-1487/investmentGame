// 도트 테마: 마우스를 클릭할 때마다 클릭한 자리에서 작은 도트 파편이 튀었다가 사라지는 효과.
// 화면 맨 위에 투명한 층을 하나 깔고(클릭은 통과), 미리 만들어 둔 작은 네모들을 돌려 쓴다.
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class PixelClickSparks : MonoBehaviour
    {
        private const int Pool = 48;          // 동시에 보일 수 있는 파편 수
        private const int PerClick = 9;       // 한 번 클릭에 튀는 파편 수
        private const float Life = 0.42f;     // 파편이 사라질 때까지(초)
        private const float Gravity = -900f;  // 아래로 떨어지는 힘(캔버스 px/초²)

        private static readonly Color32[] Colors =
        {
            new Color32(0xF2, 0xE6, 0xCC, 0xFF), // 크림
            new Color32(0xD9, 0xC7, 0xA0, 0xFF), // 베이지
            new Color32(0xE8, 0x89, 0x2B, 0xFF), // 주황
            new Color32(0xFF, 0x4D, 0x4D, 0xFF), // 빨강
            new Color32(0x5C, 0xD1, 0x7A, 0xFF), // 초록
        };

        private struct Spark { public RectTransform Rt; public Image Img; public Vector2 Vel; public float Age; public bool On; }

        private readonly Spark[] _sparks = new Spark[Pool];
        private RectTransform _layer;
        private Canvas _canvas;
        private int _next;
        private readonly System.Random _rng = new System.Random();

        /// <summary>parent가 속한 최상위 캔버스에 효과를 한 번만 붙인다.</summary>
        public static void Attach(Transform parent)
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            if (canvas.GetComponentInChildren<PixelClickSparks>(true) != null) return;
            var rt = UiKit.Rect(canvas.transform, "PixelClickSparks");
            UiKit.Stretch(rt);
            rt.SetAsLastSibling();
            var fx = rt.gameObject.AddComponent<PixelClickSparks>();
            fx._layer = rt; fx._canvas = canvas;
            fx.BuildPool();
        }

        private void BuildPool()
        {
            for (int i = 0; i < Pool; i++)
            {
                var img = UiKit.Panel(_layer, "Spark", Color.white);
                img.raycastTarget = false;
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(.5f, .5f);
                img.gameObject.SetActive(false);
                _sparks[i] = new Spark { Rt = img.rectTransform, Img = img };
            }
        }

        private void Update()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) Burst(mouse.position.ReadValue());

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < Pool; i++)
            {
                if (!_sparks[i].On) continue;
                var s = _sparks[i];
                s.Age += dt;
                if (s.Age >= Life) { s.On = false; s.Rt.gameObject.SetActive(false); _sparks[i] = s; continue; }
                s.Vel.y += Gravity * dt;
                var p = s.Rt.anchoredPosition + s.Vel * dt;
                s.Rt.anchoredPosition = new Vector2(Mathf.Round(p.x / 2f) * 2f, Mathf.Round(p.y / 2f) * 2f); // 2px 격자에 맞춰 도트처럼
                float t = s.Age / Life;
                var c = s.Img.color; c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f; s.Img.color = c;
                _sparks[i] = s;
            }
        }

        private void Burst(Vector2 screen)
        {
            if (_layer == null) return;
            var cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, cam, out var local)) return;
            _layer.SetAsLastSibling(); // 새로 연 창보다도 위에
            for (int k = 0; k < PerClick; k++)
            {
                var s = _sparks[_next]; _next = (_next + 1) % Pool;
                float angle = (float)(_rng.NextDouble() * Mathf.PI * 2);
                float speed = 120f + (float)_rng.NextDouble() * 200f;
                s.Vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.8f + 0.6f) * speed; // 살짝 위로 튐
                s.Age = 0; s.On = true;
                float size = _rng.NextDouble() < 0.3 ? 8f : 6f;
                s.Rt.sizeDelta = new Vector2(size, size);
                s.Rt.anchoredPosition = local;
                s.Img.color = Colors[_rng.Next(Colors.Length)];
                s.Rt.gameObject.SetActive(true);
                _sparks[_next == 0 ? Pool - 1 : _next - 1] = s;
            }
        }
    }
}
