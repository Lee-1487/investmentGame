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
        // 테마: 검정·흰색(무채색)만 사용. 초록(Success)만 예외로 유지.
        // 주가 등락도 무채색: 상승 = 흰색, 하락 = 회색.
        public static readonly Color Background = new Color32(0x00, 0x00, 0x00, 0xFF);
        public static readonly Color Bezel = new Color32(0x05, 0x05, 0x05, 0xFF);
        public static readonly Color Screen = new Color32(0x00, 0x00, 0x00, 0xFF);
        public static readonly Color Card = new Color32(0x0F, 0x0F, 0x0F, 0xFF);
        public static readonly Color CardBorder = new Color32(0x2A, 0x2A, 0x2A, 0xFF);
        public static readonly Color Text = new Color32(0xF5, 0xF5, 0xF5, 0xFF);
        public static readonly Color SubText = new Color32(0x8C, 0x8C, 0x8C, 0xFF);
        public static readonly Color Accent = new Color32(0xFF, 0xFF, 0xFF, 0xFF);     // 강조 = 흰색(버튼 글자는 자동으로 검정)
        public static readonly Color OnAccent = new Color32(0x00, 0x00, 0x00, 0xFF);   // 흰 강조 위 글자
        public static readonly Color AccentSoft = new Color32(0x1E, 0x1E, 0x1E, 0xFF);
        public static readonly Color Disabled = new Color32(0x1A, 0x1A, 0x1A, 0xFF);
        public static readonly Color DisabledText = new Color32(0x55, 0x55, 0x55, 0xFF);
        public static readonly Color Up = new Color32(0xFF, 0xFF, 0xFF, 0xFF);    // 상승: 흰색
        public static readonly Color Down = new Color32(0x8A, 0x8A, 0x8A, 0xFF);  // 하락: 회색
        public static readonly Color Locked = new Color32(0x8C, 0x8C, 0x8C, 0x30);
        public static readonly Color Grid = new Color32(0x2C, 0x2C, 0x2C, 0xFF);
        public static readonly Color Edge = new Color32(0xFF, 0xFF, 0xFF, 0x20);
        public static readonly Color EdgeHover = new Color32(0xFF, 0xFF, 0xFF, 0x50);
        public static readonly Color Dim = new Color32(0x00, 0x00, 0x00, 0xB0);
        public static readonly Color Warning = new Color32(0xC8, 0xC8, 0xC8, 0xFF);
        public static readonly Color Success = new Color32(0x22, 0xC5, 0x5E, 0xFF); // 초록은 유지

        /// <summary>REQ-007 개인별 색상. 플레이어 ID 순서대로.</summary>
        public static Color Player(string playerId)
        {
            switch (playerId)
            {
                // 방송 순위 그래프처럼 검정 위에서 또렷한 선 색
                // 플레이어 구분색은 무채색 테마의 예외(검정 위에서 또렷한 파랑·주황·청록·보라)
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

        private static Sprite _soft;
        /// <summary>가장자리로 갈수록 투명해지는(그라데이션) 9-slice 스프라이트. 네모 칸을 바탕에 자연스럽게 녹일 때.</summary>
        public static Sprite Soft
        {
            get
            {
                if (_soft != null) return _soft;
                const int size = 128;
                const float feather = 60f; // 가장자리에서 이 거리(px)에 걸쳐 0 → 1
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
                    float dx = Mathf.Min(x + 0.5f, size - x - 0.5f), dy = Mathf.Min(y + 0.5f, size - y - 0.5f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dx / feather)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(dy / feather));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                _soft = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(feather + 2, feather + 2, feather + 2, feather + 2));
                _soft.hideFlags = HideFlags.DontSave;
                return _soft;
            }
        }

        private static Sprite _pill;
        /// <summary>양 끝이 완전한 반원인 알약(길쭉한 원형) 버튼용 9-slice 스프라이트. 반지름 32px.</summary>
        public static Sprite Pill
        {
            get
            {
                if (_pill != null) return _pill;
                const int size = 64;
                const float radius = 32f;
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
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                _pill = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(radius - 1, radius - 1, radius - 1, radius - 1));
                _pill.hideFlags = HideFlags.DontSave;
                return _pill;
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
            button.transition = Selectable.Transition.None; // 도형·색 변화 없이 글자 + 마우스 올림 밑줄로만 표시
            var text = Label(img.transform, label, fontSize, textColor);
            Stretch(text.rectTransform, 4, 2, 4, 2);
            var look = img.gameObject.AddComponent<HoverUnderline>();
            look.Init(button, text);
            look.Apply(background, textColor);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>정수만 받는 입력 칸(최대 2자리).</summary>
        public static InputField NumberInput(Transform parent, string text, int fontSize)
        {
            // 네모 칸 대신 흰 밑줄만(투명 바탕은 클릭 판정용)
            var bg = Panel(parent, "NumberInput", Color.clear, raycast: true);
            var line = Panel(bg.transform, "Underline", Palette.Text);
            line.rectTransform.anchorMin = Vector2.zero; line.rectTransform.anchorMax = new Vector2(1, 0);
            line.rectTransform.offsetMin = Vector2.zero; line.rectTransform.offsetMax = new Vector2(0, 1.5f);
            var inner = Panel(bg.transform, "Inner", Color.clear);
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
            var t = LabelOf(button);
            t.text = label;
            var look = button.GetComponent<HoverUnderline>();
            if (look != null) look.Apply(background, textColor);
            else { ((Image)button.targetGraphic).color = background; t.color = Readable(background, textColor); }
        }

        /// <summary>밝은 바탕에 밝은 글자(예: 흰 강조 버튼 + 흰 글자)면 글자를 검정으로 바꿔 읽히게 한다.</summary>
        public static Color Readable(Color background, Color text)
        {
            float bg = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
            float fg = text.r * 0.299f + text.g * 0.587f + text.b * 0.114f;
            return bg > 0.7f && fg > 0.7f && background.a > 0.5f ? Palette.OnAccent : text;
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

    /// <summary>
    /// 버튼 바탕을 알약 모양으로: 양 끝 반원 반지름 = 짧은 변의 절반.
    /// 큰 카드형 버튼(짧은 변 60 초과)은 일반 둥근 모서리를 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PillShape : MonoBehaviour
    {
        private Image _image;
        private void Awake() { _image = GetComponent<Image>(); Apply(); }
        private void OnEnable() => Apply();
        private void OnRectTransformDimensionsChange() => Apply();

        private void Apply()
        {
            if (_image == null) _image = GetComponent<Image>();
            if (_image == null) return;
            var r = ((RectTransform)transform).rect;
            float shortSide = Mathf.Min(r.width, r.height);
            if (shortSide <= 0f) return;
            _image.type = Image.Type.Sliced;
            if (shortSide > 60f)
            {
                _image.sprite = UiKit.Rounded;
                _image.pixelsPerUnitMultiplier = 2f;
                return;
            }
            _image.sprite = UiKit.Pill;
            // 스프라이트 반지름 32px(100ppu) → 화면 반지름 = 32 / 배율. 짧은 변의 절반이 되도록.
            _image.pixelsPerUnitMultiplier = Mathf.Max(0.01f, 64f / shortSide);
        }
    }

    /// <summary>
    /// 버튼 모양: 도형(바탕) 없이 글자만 보이고, 마우스를 올리면 글자 아래 흰 밑줄이 나타난다.
    /// 바탕이 밝은(주 버튼·선택됨) 경우는 글자를 굵게 해 구분한다.
    /// 로고·그림 등 자식 요소가 있는 카드형 버튼은 원래 바탕을 유지한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoverUnderline : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Button _button;
        private Text _label;
        private Image _image, _line;
        private Color _background, _text;
        private bool _hover;

        public void Init(Button button, Text label)
        {
            _button = button; _label = label; _image = (Image)button.targetGraphic;
            _line = UiKit.Panel(transform, "HoverUnderline", Palette.Text);
            _line.rectTransform.anchorMin = _line.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _line.rectTransform.pivot = new Vector2(0.5f, 1f);
            _line.gameObject.SetActive(false);
        }

        /// <summary>카드형: 글자 말고 다른 자식(로고·그림 등)이 붙은 버튼.</summary>
        private bool IsCard
        {
            get
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    var c = transform.GetChild(i);
                    if (c != _label.transform && c != _line.transform) return true;
                }
                return false;
            }
        }

        public void Apply(Color background, Color text)
        {
            _background = background; _text = text;
            if (_image == null || _label == null) return;
            if (IsCard) { _image.color = background; _label.color = UiKit.Readable(background, text); return; }
            _image.color = Color.clear; // 클릭 판정은 그대로(투명 바탕)
            float lum = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
            bool primary = background.a > 0.5f && lum > 0.7f; // 흰 강조 바탕 = 주 버튼/선택됨
            bool disabled = _button != null && !_button.interactable;
            _label.color = disabled ? Palette.DisabledText : primary ? Palette.Text : text;
            _label.fontStyle = primary ? FontStyle.Bold : FontStyle.Normal;
            Refresh();
        }

        private bool _lastInteractable = true;
        private void Start() { Apply(_background, _text); _lastInteractable = _button == null || _button.interactable; } // 자식(로고 등)이 다 붙은 뒤 카드형 여부 다시 판단
        private void Update()
        {
            // interactable을 SetButton 없이 바꾼 경우에도 글자색(비활성 회색)을 맞춘다
            bool now = _button == null || _button.interactable;
            if (now == _lastInteractable) return;
            _lastInteractable = now; Apply(_background, _text);
        }
        private void OnDisable() { _hover = false; Refresh(); }
        public void OnPointerEnter(PointerEventData e) { _hover = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hover = false; Refresh(); }

        private void Refresh()
        {
            if (_line == null || _label == null) return;
            bool show = _hover && (_button == null || _button.interactable) && !string.IsNullOrEmpty(_label.text) && !IsCard;
            _line.gameObject.SetActive(show);
            if (!show) return;
            var rect = ((RectTransform)transform).rect;
            float w = Mathf.Min(_label.preferredWidth, rect.width - 8f);
            // 글자 아래로 살짝 띄운다(글자 크기의 약 0.8 + 4px). 버튼 칸 밖으로는 조금(최대 6px)까지만.
            float y = -Mathf.Min(rect.height * 0.5f + 6f, _label.fontSize * 0.8f + 4f);
            _line.rectTransform.anchoredPosition = new Vector2(0, y);
            _line.rectTransform.sizeDelta = new Vector2(Mathf.Max(8f, w), 1.5f);
        }
    }
}
