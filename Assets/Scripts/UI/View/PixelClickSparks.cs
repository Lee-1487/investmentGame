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
        private const float Life = 0.95f;     // 파편이 사라질 때까지(초): 튀어 올랐다 떨어지는 게 보이게
        private const float Gravity = -1100f; // 아래로 떨어지는 힘(캔버스 px/초²)

        private static readonly Color32 SparkColor = new Color32(0xA0, 0xA0, 0xA0, 0xFF); // 회색

        private struct Spark { public RectTransform Rt; public Image Img; public Vector2 Vel; public float Age, Floor; public int Bounces; public bool On; }

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
                s.Vel.y += Gravity * dt;
                var p = s.Rt.anchoredPosition + s.Vel * dt;
                if (p.y < s.Floor && s.Vel.y < 0 && s.Bounces < 2)
                {
                    // 바닥(클릭 지점 조금 아래)에 닿으면 통통 튄다
                    p.y = s.Floor; s.Vel.y = -s.Vel.y * 0.45f; s.Vel.x *= 0.7f; s.Bounces++;
                }
                s.Rt.anchoredPosition = new Vector2(Mathf.Round(p.x / 2f) * 2f, Mathf.Round(p.y / 2f) * 2f); // 2px 격자에 맞춰 도트처럼
                float t = s.Age / Life;
                var c = s.Img.color; c.a = t < 0.65f ? 1f : 1f - (t - 0.65f) / 0.35f; s.Img.color = c;
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
                // 누를 때마다 똑같은 모양: 부채꼴로 고르게 퍼지고(가운데가 가장 높이) 같은 높이에서 통통
                float u = PerClick > 1 ? k / (float)(PerClick - 1) : 0.5f;   // 0 ~ 1
                float side = u * 2f - 1f;                                     // -1(왼쪽) ~ 1(오른쪽)
                s.Vel = new Vector2(side * 340f, 260f + 280f * (1f - Mathf.Abs(side)));
                s.Age = 0; s.On = true; s.Bounces = 0;
                s.Floor = local.y - 64f;
                float size = k % 2 == 0 ? 4f : 2f;
                s.Rt.sizeDelta = new Vector2(size, size);
                s.Rt.anchoredPosition = local;
                s.Img.color = SparkColor;
                s.Rt.gameObject.SetActive(true);
                _sparks[_next == 0 ? Pool - 1 : _next - 1] = s;
            }
        }
    }
}
