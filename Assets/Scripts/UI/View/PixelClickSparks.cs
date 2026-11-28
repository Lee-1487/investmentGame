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
        private const float Life = 0.6f;      // 파편이 흐려지며 사라질 때까지(초)
        private const float Drag = 5f;        // 점점 느려지는 정도(원형으로 퍼졌다가 멈추듯 사라짐)

        private const float Speed = 240f;   // 튀는 세기(모든 파편 같음 → 범위 일정)
        private readonly System.Random _rng = new System.Random();
        private static readonly Color32 SparkColor = new Color32(0xA0, 0xA0, 0xA0, 0xFF); // 회색

        private struct Spark { public RectTransform Rt; public Image Img; public Vector2 Vel, Pos; public float Age, Floor; public int Bounces; public bool On; }

        private readonly Spark[] _sparks = new Spark[Pool];
        private RectTransform _layer;
        private Canvas _canvas;
        private int _next;

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
                s.Vel *= Mathf.Exp(-Drag * dt);
                // 실제 위치는 소수점까지 따로 기억하고, 화면에 그릴 때만 2px 칸에 맞춘다
                // (예전엔 칸에 맞춘 값을 다시 위치로 써서, 프레임이 빠르면 한 프레임 이동이 1px 미만이라 제자리에 멈췄음)
                var p = s.Pos + s.Vel * dt;
                s.Pos = p;
                s.Rt.anchoredPosition = new Vector2(Mathf.Round(p.x / 2f) * 2f, Mathf.Round(p.y / 2f) * 2f); // 2px 격자에 맞춰 도트처럼
                float t = s.Age / Life;
                var c = s.Img.color; c.a = 1f - t * t; s.Img.color = c; // 점점 흐려지며 사라짐
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
                // 원형으로: 방향은 360도 무작위, 튀는 세기(범위)는 항상 같게
                float angle = (float)(_rng.NextDouble() * Mathf.PI * 2);
                s.Vel = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Speed;
                s.Age = 0; s.On = true; s.Bounces = 0;
                float size = _rng.NextDouble() < 0.4 ? 2f : 4f;
                s.Rt.sizeDelta = new Vector2(size, size);
                s.Pos = local; s.Rt.anchoredPosition = local;
                s.Img.color = SparkColor;
                s.Rt.gameObject.SetActive(true);
                _sparks[_next == 0 ? Pool - 1 : _next - 1] = s;
            }
        }
    }
}
