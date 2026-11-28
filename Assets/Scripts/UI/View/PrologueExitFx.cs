// 스토리(프롤로그) 마지막 초대장에서 방 만들기 화면으로 넘어갈 때의 '입장' 연출.
// 스토리 코드는 건드리지 않고, 스토리 화면(PrologueIntro)이 꺼지는 순간을 지켜보다가
// 마지막 장면(초대장) 그림을 화면 위에 복사해 띄운 뒤, 확대되며 흐려지고(번짐) 사라지게 한다.
// 그 아래로 방 만들기 화면이 살짝 작았다가 제 크기로 커지며 나타난다.
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class PrologueExitFx : MonoBehaviour
    {
        private const float Seconds = 2.2f; // 천천히 시작해 점점 빨라짐
        private Canvas _canvas;
        private bool _wasShowing;

        /// <summary>parent가 속한 최상위 캔버스에 한 번만 붙인다.</summary>
        public static void Attach(Transform parent)
        {
            var canvas = parent.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas = canvas.rootCanvas;
            if (canvas.GetComponentInChildren<PrologueExitFx>(true) != null) return;
            var rt = UiKit.Rect(canvas.transform, "PrologueExitFx");
            UiKit.Stretch(rt);
            var fx = rt.gameObject.AddComponent<PrologueExitFx>();
            fx._canvas = canvas;
            var story = canvas.transform.Find("PrologueIntro");
            fx._wasShowing = story != null && story.gameObject.activeInHierarchy;
        }

        private void LateUpdate()
        {
            if (_canvas == null) return;
            var story = _canvas.transform.Find("PrologueIntro");
            bool showing = story != null && story.gameObject.activeInHierarchy;
            if (_wasShowing && !showing) Play(story);
            _wasShowing = showing;
        }

        private void Play(Transform story)
        {
            var source = story != null ? story.Find("PrologueImage")?.GetComponent<Image>() : null;
            if (source == null || source.sprite == null) return;

            var layer = UiKit.Rect(transform, "Exit");
            UiKit.Stretch(layer);
            transform.SetAsLastSibling();
            var group = layer.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;

            // 검은 바탕(처음엔 그대로 덮었다가 함께 사라짐)
            var black = UiKit.Panel(layer, "Black", Color.black); UiKit.Stretch(black.rectTransform);

            // 초대장 그림: 가운데 1장 + 번짐용으로 사방으로 밀리는 흐린 복사본 4장
            var copies = new RectTransform[5];
            for (int i = 0; i < copies.Length; i++)
            {
                var rt = UiKit.Rect(layer, i == 0 ? "Invite" : "Blur" + i);
                UiKit.Stretch(rt);
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = source.sprite; img.raycastTarget = false;
                img.color = new Color(1, 1, 1, i == 0 ? 1f : 0.28f);
                var fit = rt.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = 16f / 9f;
                copies[i] = rt;
            }

            // 방 만들기 화면 전체(바탕 포함)를 줄이면 가장자리에 Unity 기본 배경이 비쳐 보이므로,
            // 바탕은 그대로 두고 가운데 카드(테두리 상자·내용)만 살짝 작았다 커지게 한다.
            var screen = _canvas.transform.Find("ConnectionScreen");
            var connection = new[] { screen?.Find("ConnectionFrame") as RectTransform, screen?.Find("ConnectionCard") as RectTransform };
            var anim = layer.gameObject.AddComponent<ExitAnimation>();
            anim.Init(group, black, copies, connection);
            Destroy(layer.gameObject, Seconds + 0.1f);
        }

        private sealed class ExitAnimation : MonoBehaviour
        {
            private CanvasGroup _group;
            private Image _black;
            private RectTransform[] _copies;
            private RectTransform[] _connection;
            private float _t;
            private static readonly Vector2[] Dirs = { Vector2.zero, new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) };

            public void Init(CanvasGroup group, Image black, RectTransform[] copies, RectTransform[] connection)
            {
                _group = group; _black = black; _copies = copies; _connection = connection;
                Apply(0f);
            }

            private void Update()
            {
                _t += Time.unscaledDeltaTime;
                Apply(Mathf.Clamp01(_t / Seconds));
            }

            private void OnDestroy() { foreach (var c in _connection) if (c != null) c.localScale = Vector3.one; }

            private void Apply(float p)
            {
                float ease = p * p * p;                              // 처음엔 아주 천천히, 갈수록 빨라짐(가속)
                float scale = Mathf.Lerp(1f, 1.35f, ease);           // 초대장 안으로 들어가듯 확대
                float spread = Mathf.Lerp(0f, 18f, ease);            // 번짐(복사본이 사방으로 벌어짐)
                for (int i = 0; i < _copies.Length; i++)
                {
                    _copies[i].localScale = new Vector3(scale, scale, 1f);
                    _copies[i].anchoredPosition = Dirs[i] * spread;
                }
                _group.alpha = 1f - Mathf.Pow(Mathf.InverseLerp(0.2f, 1f, p), 2f);            // 흐려지며 사라짐(끝으로 갈수록 빠르게)
                _black.color = new Color(0, 0, 0, 1f - Mathf.InverseLerp(0.3f, 0.8f, p));
                float c = Mathf.Lerp(0.94f, 1f, ease);               // 방 만들기 카드가 살짝 작았다가 제 크기로
                foreach (var rt in _connection) if (rt != null) rt.localScale = new Vector3(c, c, 1f);
            }
        }
    }
}
