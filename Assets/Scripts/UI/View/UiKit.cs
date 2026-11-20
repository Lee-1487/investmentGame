// 코드로 uGUI를 만드는 작은 도구 모음.
// Prefab 대신 코드로 화면을 구성해 두면 데이터 연결을 먼저 검증할 수 있다.
// 디자인이 확정되면 같은 구조를 Prefab으로 옮길 수 있다.
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public static class Palette
    {
        // 테마: 트레이딩 대회 방송 화면처럼 검정 바탕 + 흰 글자 + 큰 숫자 + 주황 포인트.
        // 주가 색은 한국식(상승 빨강·하락 파랑)을 유지하되 검정 위에서 잘 보이게 밝게.
        public static readonly Color Background = new Color32(0x00, 0x00, 0x00, 0xFF);
        public static readonly Color Bezel = new Color32(0x05, 0x05, 0x06, 0xFF);
        public static readonly Color Screen = new Color32(0x00, 0x00, 0x00, 0xFF);
        public static readonly Color Card = new Color32(0x0E, 0x0F, 0x11, 0xFF);
        public static readonly Color CardBorder = new Color32(0x26, 0x28, 0x2D, 0xFF);
        public static readonly Color Text = new Color32(0xF5, 0xF5, 0xF5, 0xFF);
        public static readonly Color SubText = new Color32(0x8B, 0x90, 0x99, 0xFF);
        public static readonly Color Accent = new Color32(0xFF, 0x6A, 0x1F, 0xFF);     // 주황 포인트(방송 화면의 강조 태그 색)
        public static readonly Color AccentSoft = new Color32(0x2A, 0x17, 0x0C, 0xFF); // 주황 버튼 바탕(어두운 주황)
        public static readonly Color Disabled = new Color32(0x1A, 0x1B, 0x1E, 0xFF);
        public static readonly Color DisabledText = new Color32(0x4E, 0x52, 0x5A, 0xFF);
        public static readonly Color Up = new Color32(0xFF, 0x4D, 0x4F, 0xFF);    // 상승(빨강)
        public static readonly Color Down = new Color32(0x3D, 0x8B, 0xFF, 0xFF);  // 하락(파랑)
        public static readonly Color Locked = new Color32(0x8B, 0x90, 0x99, 0x30);
        public static readonly Color Grid = new Color32(0x2A, 0x2D, 0x33, 0xFF);
        public static readonly Color Edge = new Color32(0xFF, 0x4D, 0x4F, 0x30);
        public static readonly Color EdgeHover = new Color32(0xFF, 0x4D, 0x4F, 0x70);
        public static readonly Color Dim = new Color32(0x00, 0x00, 0x00, 0xB0);
        public static readonly Color Warning = new Color32(0xF5, 0x9E, 0x0B, 0xFF);
        public static readonly Color Success = new Color32(0x22, 0xC5, 0x5E, 0xFF);

        /// <summary>REQ-007 개인별 색상. 플레이어 ID 순서대로.</summary>
        public static Color Player(string playerId)
        {
            switch (playerId)
            {
                // 방송 순위 그래프처럼 검정 위에서 또렷한 선 색
                case "P1": return new Color32(0x4D, 0xA3, 0xFF, 0xFF);
                case "P2": return new Color32(0xFF, 0xB0, 0x20, 0xFF);
                case "P3": return new Color32(0x2D, 0xD4, 0xBF, 0xFF);
                case "P4": return new Color32(0xC0, 0x84, 0xFC, 0xFF);
                default: return Color.gray;
            }
        }

        public static Color ForBps(int bps) => bps > 0 ? Up : bps < 0 ? Down : Text;
    }

    public static class UiKit
    {
        public static RectTransform ScrollContent(RectTransform viewport, string name)
        {
            var image = viewport.gameObject.AddComponent<Image>(); image.color = new Color(1, 1, 1, .01f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect(viewport, name);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24;
            return content;
        }
        private static Font _font;
        private static Sprite _rounded;

        /// <summary>한글이 보이는 OS 폰트(맑은 고딕 등). 없으면 Unity 기본 폰트.</summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                try
                {
                    _font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Pretendard", "Pretendard Variable", "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "Arial" }, 16);
                }
                catch (Exception)
                {
                    _font = null;
                }
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>코드로 만든 둥근 모서리 9-slice 스프라이트.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded != null) return _rounded;
                const int size = 64;
                const float radius = 10f; // 방송 화면처럼 모서리를 덜 둥글게
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                _rounded = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                _rounded.hideFlags = HideFlags.DontSave;
                return _rounded;
            }
        }

        // ------------------------------------------------------------------
        // 배치
        // ------------------------------------------------------------------

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>부모 전체를 채우되 왼쪽·위·오른쪽·아래 여백을 둔다.</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>앵커 비율 영역(0~1)에 여백 없이 배치.</summary>
        public static RectTransform Anchor(RectTransform rt, float minX, float minY, float maxX, float maxY,
            float padX = 0, float padY = 0)
        {
            rt.anchorMin = new Vector2(minX, minY);
            rt.anchorMax = new Vector2(maxX, maxY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padX, padY);
            rt.offsetMax = new Vector2(-padX, -padY);
            return rt;
        }

        /// <summary>부모의 한 점(앵커)에 고정 크기로 배치. pos는 그 점으로부터의 거리.</summary>
        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        // ------------------------------------------------------------------
        // 요소
        // ------------------------------------------------------------------

        public static Image Panel(Transform parent, string name, Color color, bool rounded = false, bool raycast = false)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            if (rounded)
            {
                img.sprite = Rounded;
                img.type = Image.Type.Sliced;
                img.pixelsPerUnitMultiplier = 2f;
            }
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color,
            TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            Stretch(rt);
            return t;
        }

        public static Button Button(Transform parent, string label, int fontSize, Color background, Color textColor,
            Action onClick, bool rounded = true)
        {
            var img = Panel(parent, "Button", background, rounded, raycast: true);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.85f, 0.85f, 0.85f, 0.6f);
            button.colors = colors;
            var text = Label(img.transform, label, fontSize, textColor);
            Stretch(text.rectTransform, 4, 2, 4, 2);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>정수만 받는 입력 칸(최대 2자리).</summary>
        public static InputField NumberInput(Transform parent, string text, int fontSize)
        {
            // 바깥(테두리 색) 안에 흰 칸을 겹쳐 테두리처럼 보이게 한다
            var bg = Panel(parent, "NumberInput", Palette.CardBorder, rounded: true, raycast: true);
            var inner = Panel(bg.transform, "Inner", Palette.Card, rounded: true);
            Stretch(inner.rectTransform, 1, 1, 1, 1);

            var value = Label(inner.transform, "", fontSize, Palette.Text);
            value.supportRichText = false; // InputField 요구사항
            value.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(value.rectTransform, 6, 2, 6, 2);

            var placeholder = Label(inner.transform, "0~30", fontSize - 3, Palette.DisabledText);
            Stretch(placeholder.rectTransform, 6, 2, 6, 2);

            var field = bg.gameObject.AddComponent<InputField>();
            field.targetGraphic = bg;
            field.textComponent = value;
            field.placeholder = placeholder;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.lineType = InputField.LineType.SingleLine;
            field.characterLimit = 2;
            field.text = text;
            return field;
        }

        public static Text LabelOf(Button button) => button.GetComponentInChildren<Text>();

        public static void SetButton(Button button, string label, bool interactable, Color background, Color textColor)
        {
            button.interactable = interactable;
            ((Image)button.targetGraphic).color = background;
            var t = LabelOf(button);
            t.text = label;
            t.color = textColor;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var child = t.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(child);
                else UnityEngine.Object.DestroyImmediate(child);
            }
        }

        public static HoverRelay Hover(GameObject go, Action enter, Action exit)
        {
            if (!go.TryGetComponent<HoverRelay>(out var relay)) relay = go.AddComponent<HoverRelay>();
            relay.Enter = enter;
            relay.Exit = exit;
            return relay;
        }

        public static ClickRelay Click(GameObject go, Action click)
        {
            if (!go.TryGetComponent<ClickRelay>(out var relay)) relay = go.AddComponent<ClickRelay>();
            relay.Clicked = click;
            return relay;
        }
    }

    public sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action Enter;
        public Action Exit;
        public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
        public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
    }

    public sealed class ClickRelay : MonoBehaviour, IPointerClickHandler
    {
        public Action Clicked;
        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}
