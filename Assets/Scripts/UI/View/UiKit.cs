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
        // 테마: 도트(픽셀) 레트로 — 어두운 갈색 바탕 + 황토색 테두리 + 베이지 버튼 + 자주색 강조.
        public static readonly Color Background = new Color32(0x15, 0x10, 0x0B, 0xFF); // 바탕(아주 어두운 갈색)
        public static readonly Color Bezel = new Color32(0x15, 0x10, 0x0B, 0xFF);
        public static readonly Color Screen = new Color32(0x1E, 0x15, 0x0E, 0xFF);     // 창·모달 바탕
        public static readonly Color Card = new Color32(0x2A, 0x1D, 0x12, 0xFF);       // 카드(패널) 바탕
        public static readonly Color ChartFrame = new Color32(0x4A, 0x36, 0x20, 0xFF); // 그래프 칸 테두리
        public static readonly Color CardBorder = new Color32(0x6B, 0x4F, 0x2C, 0xFF); // 패널 테두리(황토)
        public static readonly Color Text = new Color32(0xF2, 0xE6, 0xCC, 0xFF);       // 크림색 글자
        public static readonly Color SubText = new Color32(0xB3, 0x9C, 0x74, 0xFF);
        public static readonly Color Accent = new Color32(0xD9, 0xC7, 0xA0, 0xFF);     // 베이지(주 버튼·턴 상자)
        public static readonly Color OnAccent = new Color32(0x2B, 0x1C, 0x0F, 0xFF);   // 베이지 위 글자(진한 갈색)
        public static readonly Color AccentSoft = new Color32(0x33, 0x24, 0x16, 0xFF);
        public static readonly Color Disabled = new Color32(0x24, 0x19, 0x10, 0xFF);
        public static readonly Color DisabledText = new Color32(0x7A, 0x67, 0x48, 0xFF);
        public static readonly Color Up = new Color32(0xFF, 0x4D, 0x4F, 0xFF);    // 상승: 빨강(한국식)
        public static readonly Color Down = new Color32(0x3D, 0x8B, 0xFF, 0xFF);  // 하락: 파랑
        public static readonly Color Locked = new Color32(0xB3, 0x9C, 0x74, 0x30);
        public static readonly Color Grid = new Color32(0x3A, 0x2A, 0x1A, 0xFF);
        public static readonly Color Edge = new Color32(0xF2, 0xE6, 0xCC, 0x20);
        public static readonly Color EdgeHover = new Color32(0xF2, 0xE6, 0xCC, 0x50);
        public static readonly Color Dim = new Color32(0x0A, 0x07, 0x04, 0xC0);
        public static readonly Color Warning = new Color32(0xE8, 0x89, 0x2B, 0xFF);   // 주황
        public static readonly Color Success = new Color32(0x3D, 0xA3, 0x5A, 0xFF);   // 초록
        // 자주색(선택된 탭·알림 상자)
        public static readonly Color Maroon = new Color32(0x4A, 0x16, 0x22, 0xFF);
        public static readonly Color MaroonBorder = new Color32(0x7A, 0x28, 0x38, 0xFF);
        public static readonly Color MaroonText = new Color32(0xF5, 0xC7, 0xC7, 0xFF);
        public static readonly Color TimerRed = new Color32(0xFF, 0x4D, 0x4D, 0xFF);
        // 중요 표시 글자색: 초록 = 잘 됨(준비 완료·확정·내 차례), 빨강 = 주의(오류·연결 끊김·마감 임박)
        public const string GoodHex = "#5CD17A", BadHex = "#FF5A5A";
        public static readonly Color Good = new Color32(0x5C, 0xD1, 0x7A, 0xFF);
        public static readonly Color Bad = new Color32(0xFF, 0x5A, 0x5A, 0xFF);
        public static string GoodText(string s) => "<color=" + GoodHex + ">" + s + "</color>";
        public static string BadText(string s) => "<color=" + BadHex + ">" + s + "</color>";

        /// <summary>REQ-007 개인별 색상. 플레이어 ID 순서대로.</summary>
        public static Color Player(string playerId)
        {
            switch (playerId)
            {
                // 방송 순위 그래프처럼 검정 위에서 또렷한 선 색
                // 플레이어 구분색은 무채색 테마의 예외. 파스텔(밝고 흐린 색)·원색 대신
                // 명도를 낮추고 채도는 유지한 깊은 색(봉의 빨강·파랑과 겹치지 않게 그 계열은 피함)
                case "P1": return new Color32(0x0B, 0x7A, 0x75, 0xFF); // 딥 틸
                case "P2": return new Color32(0xA8, 0x51, 0x0F, 0xFF); // 구리색(번트 오렌지)
                case "P3": return new Color32(0x6A, 0x2C, 0x9E, 0xFF); // 딥 퍼플
                case "P4": return new Color32(0x2E, 0x7D, 0x32, 0xFF); // 포레스트 그린
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

        private static Font _bold;
        /// <summary>도트 폰트(갈무리11, OFL). 없으면 한글 OS 폰트 → Unity 기본 폰트.</summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Resources.Load<Font>("Fonts/Galmuri11");
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

        /// <summary>굵은 도트 폰트(갈무리11 Bold). 없으면 일반 폰트(굵기는 Unity가 흉내 냄).</summary>
        public static Font BoldFont
        {
            get
            {
                if (_bold != null) return _bold;
                _bold = Resources.Load<Font>("Fonts/Galmuri11-Bold");
                return _bold;
            }
        }

        /// <summary>도트 테두리 한 칸(텍스처 1픽셀)이 화면에서 차지하는 크기 = 1 / 이 값.</summary>
        public const float PixelScale = 0.5f;

        // 도트 모서리 스프라이트: 계단식으로 깎인 모서리, 점(Point) 필터.
        private static Sprite PixelSprite(int size, int notch, int border, float rimShade, int bevel, float bevelShade, bool hollow)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int dx = Mathf.Min(x, size - 1 - x), dy = Mathf.Min(y, size - 1 - y);
                bool cut = dx + dy < notch; // 모서리 계단
                if (cut) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                bool rim = dx < border || dy < border || dx + dy < notch + border;
                if (hollow && !rim) { px[y * size + x] = new Color32(0, 0, 0, 0); continue; }
                float v = rim && !hollow ? rimShade : 1f;
                if (!hollow && !rim && y < border + bevel) v = bevelShade; // 아래쪽 그림자(입체감)
                byte c = (byte)(v * 255);
                px[y * size + x] = new Color32(c, c, c, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            int edge = notch + border + bevel + 1;
            var sp = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(edge, edge, edge, edge));
            sp.hideFlags = HideFlags.DontSave;
            return sp;
        }

        private static Sprite _frame, _bevel;
        /// <summary>속이 빈 도트 테두리(버튼 외곽선용).</summary>
        public static Sprite PixelFrame => _frame != null ? _frame : (_frame = PixelSprite(16, 2, 2, 1f, 0, 1f, true));
        /// <summary>아래쪽이 살짝 어두운 도트 버튼 바탕(입체감).</summary>
        public static Sprite PixelBevel => _bevel != null ? _bevel : (_bevel = PixelSprite(16, 2, 0, 1f, 3, 0.72f, false));

        private static Sprite _face;
        /// <summary>
        /// 플레이어 얼굴 도트 아이콘(14x14). 흰 몸통은 Image 색(플레이어 색)으로 물들고,
        /// 외곽선·눈·입은 어두워서 어떤 색에서도 보인다.
        /// </summary>
        public static Sprite PixelFace
        {
            get
            {
                if (_face != null) return _face;
                string[] art =
                {
                    "..............",
                    "....######....",
                    "..##wwwwww##..",
                    ".#wwhhwwwwww#.",
                    ".#whhwwwwwww#.",
                    "#wwwwwwwwwwww#",
                    "#ww##wwww##ww#",
                    "#ww#kwwww#kww#",
                    "#wwwwwwwwwwww#",
                    "#wwww####wwww#",
                    ".#wwwwwwwwww#.",
                    ".#ssssssssss#.",
                    "..##########..",
                    "..............",
                };
                const int size = 14;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    char ch = art[size - 1 - y][x];
                    px[y * size + x] = ch == '#' ? new Color32(0x14, 0x0E, 0x09, 255)   // 외곽선·눈·입
                        : ch == 'k' ? new Color32(0xF2, 0xE6, 0xCC, 255)               // 눈 반짝임
                        : ch == 'h' ? new Color32(255, 255, 255, 255)                    // 하이라이트(색이 가장 밝게)
                        : ch == 'w' ? new Color32(0xD8, 0xD8, 0xD8, 255)                 // 몸통(플레이어 색)
                        : ch == 's' ? new Color32(0x8C, 0x8C, 0x8C, 255)                 // 아래 그림자
                        : new Color32(0, 0, 0, 0);
                }
                tex.SetPixels32(px); tex.Apply();
                _face = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
                _face.hideFlags = HideFlags.DontSave;
                return _face;
            }
        }

        /// <summary>도트 모서리(계단식으로 깎인) 9-slice 스프라이트. 예전 '둥근 모서리' 자리에 그대로 쓰인다.</summary>
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = PixelSprite(16, 2, 0, 1f, 0, 1f, false));

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

        private static Sprite _coin;
        /// <summary>코드로 그린 금화 아이콘(64px). 바깥 테두리 + 안쪽 홈 + 왼쪽 위 반사광.</summary>
        public static Sprite Coin
        {
            get
            {
                if (_coin != null) return _coin;
                const int size = 64;
                const float radius = 31f;
                var rim = new Color(0.72f, 0.50f, 0.10f);   // 테두리(진한 금색)
                var face = new Color(0.98f, 0.78f, 0.20f);  // 앞면(밝은 금색)
                var groove = new Color(0.82f, 0.60f, 0.12f); // 안쪽 홈
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave
                };
                var pixels = new Color32[size * size];
                var center = new Vector2(size * 0.5f, size * 0.5f);
                var light = new Vector2(size * 0.36f, size * 0.66f);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float d = Vector2.Distance(p, center);
                    float alpha = Mathf.Clamp01(radius - d + 0.5f);
                    Color c = d > radius - 5f ? rim : face;
                    if (Mathf.Abs(d - (radius - 11f)) < 1.5f) c = groove;
                    // 반사광: 왼쪽 위가 살짝 밝다
                    float shine = Mathf.Clamp01(1f - Vector2.Distance(p, light) / (radius * 0.9f)) * 0.35f;
                    c = Color.Lerp(c, Color.white, shine);
                    c.a = alpha;
                    pixels[y * size + x] = c;
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                _coin = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
                _coin.hideFlags = HideFlags.DontSave;
                return _coin;
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
                img.pixelsPerUnitMultiplier = PixelScale;
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
            SetStyle(t, style);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.supportRichText = true;
            Stretch(rt);
            return t;
        }

        /// <summary>도트 테마: 패널 가장자리에 속이 빈 도트 테두리를 덧씌운다(바로 다음 자식으로).</summary>
        public static Image AddFrame(Graphic panel, Color border)
        {
            var frame = Panel(panel.transform, "PixelFrame", border);
            frame.sprite = PixelFrame; frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = PixelScale;
            Stretch(frame.rectTransform);
            frame.transform.SetAsFirstSibling();
            return frame;
        }

        /// <summary>굵게: 굵은 도트 폰트가 있으면 그 폰트로 바꾸고(번지지 않게), 없으면 Unity 굵기.</summary>
        public static void SetStyle(Text t, FontStyle style)
        {
            bool bold = style == FontStyle.Bold || style == FontStyle.BoldAndItalic;
            if (bold && BoldFont != null) { t.font = BoldFont; t.fontStyle = style == FontStyle.BoldAndItalic ? FontStyle.Italic : FontStyle.Normal; }
            else { t.font = Font; t.fontStyle = style; }
        }

        public static Button Button(Transform parent, string label, int fontSize, Color background, Color textColor,
            Action onClick, bool rounded = true)
        {
            var img = Panel(parent, "Button", background, rounded, raycast: true);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.transition = Selectable.Transition.None; // 색 변화는 HoverUnderline이 직접 처리(회색 둥근 네모)
            var text = Label(img.transform, label, fontSize, textColor);
            Stretch(text.rectTransform, 4, 2, 4, 2);
            var look = img.gameObject.AddComponent<HoverUnderline>();
            look.Init(button, text);
            img.gameObject.AddComponent<PressFeedback>(); // 누르는 동안 쑥 들어가는 눌림 효과
            look.Apply(background, textColor);
            button.onClick.AddListener(() => UiSound.Play(UiSound.Kind.Click)); // 버튼 효과음(플레이 중에만)
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>정수만 받는 입력 칸(최대 2자리).</summary>
        public static InputField NumberInput(Transform parent, string text, int fontSize)
        {
            // 도트 테마: 황토 테두리 + 아주 어두운 입력 칸
            var bg = Panel(parent, "NumberInput", Palette.CardBorder, rounded: true, raycast: true);
            var inner = Panel(bg.transform, "Inner", new Color32(0x1A, 0x12, 0x0C, 0xFF), rounded: true);
            Stretch(inner.rectTransform, 2, 2, 2, 2);

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
            if (!go.TryGetComponent<PressFeedback>(out _)) go.AddComponent<PressFeedback>(); // 클릭되는 칸도 눌림 효과
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

    /// <summary>
    /// 눌림 효과: 마우스를 누르는 동안 살짝 작아지며(쑥 들어감), 떼면 톡 튀어나왔다 원래 크기로.
    /// 누를 수 없는(비활성) 버튼에는 반응하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        private const float PressedScale = 0.92f, PopScale = 1.04f;
        private Selectable _selectable;
        private bool _pressed;
        private float _scale = 1f, _target = 1f;

        private void Awake() => _selectable = GetComponent<Selectable>();
        // 화면 전체를 덮는 큰 클릭 영역(예: 처음 스토리 화면)은 눌림 효과 없음
        private bool Usable
        {
            get
            {
                if (_selectable != null && !_selectable.IsInteractable()) return false;
                var size = ((RectTransform)transform).rect.size;
                return size.x <= 600f && size.y <= 400f;
            }
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !Usable) return;
            _pressed = true; _target = PressedScale;
            _scale = PressedScale; Apply(); // 누르는 순간 바로 들어가 보이게
            if (TryGetComponent<HoverUnderline>(out var look)) look.SetPressed(true);
        }

        public void OnPointerUp(PointerEventData e) => Release(true);
        public void OnPointerExit(PointerEventData e) { if (_pressed) Release(false); }

        private void Release(bool pop)
        {
            if (!_pressed) return;
            _pressed = false;
            _scale = pop ? PopScale : _scale; _target = 1f; Apply();
            if (TryGetComponent<HoverUnderline>(out var look)) look.SetPressed(false);
        }

        private void OnDisable() { _pressed = false; _scale = _target = 1f; Apply(); }

        private void Update()
        {
            if (Mathf.Approximately(_scale, _target)) return;
            _scale = Mathf.MoveTowards(_scale, _target, Time.unscaledDeltaTime * 0.9f);
            Apply();
        }

        private void Apply() => transform.localScale = new Vector3(_scale, _scale, 1f);
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
    /// 버튼 모양(도트 테마): 계단식 모서리 + 황토색 외곽선 + 아래쪽 그림자.
    /// 주 버튼(밝은 바탕) = 베이지 바탕 + 진한 갈색 굵은 글자, 보조 버튼 = 어두운 갈색 바탕 + 크림색 글자,
    /// 자주색 바탕 = 선택된 탭. 비활성은 더 어둡게. 마우스를 올리면 조금 밝아진다.
    /// 로고·그림 등 자식 요소가 있는 카드형 버튼은 원래 바탕을 유지한다.
    /// (이름은 예전 '마우스 올림 밑줄' 시절 그대로 둔다: 다른 코드가 참조함)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoverUnderline : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Button _button;
        private Text _label;
        private Image _image, _line, _frame;
        private Color _background, _text;
        private bool _hover, _pressed;

        /// <summary>누르는 동안: 바탕을 어둡게 하고 글자를 2px 내려 '눌린' 느낌.</summary>
        public void SetPressed(bool on)
        {
            if (_pressed == on) return;
            _pressed = on;
            if (_label != null && !IsCard)
            {
                var rt = _label.rectTransform;
                rt.anchoredPosition += new Vector2(0, on ? -2f : 2f);
            }
            Refresh();
        }

        public void Init(Button button, Text label)
        {
            _button = button; _label = label; _image = (Image)button.targetGraphic;
            _line = UiKit.Panel(transform, "HoverUnderline", Palette.Text); // 예전 밑줄(사용 안 함, 참조 호환용)
            _line.gameObject.SetActive(false);
            _frame = UiKit.Panel(transform, "PixelFrame", Palette.CardBorder);
            _frame.sprite = UiKit.PixelFrame; _frame.type = Image.Type.Sliced; _frame.pixelsPerUnitMultiplier = UiKit.PixelScale;
            UiKit.Stretch(_frame.rectTransform);
            _frame.transform.SetAsFirstSibling(); // 글자 뒤
            _frame.gameObject.SetActive(false);
        }

        /// <summary>카드형: 글자 말고 다른 자식(로고·그림 등)이 붙은 버튼.</summary>
        private bool IsCard
        {
            get
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    var c = transform.GetChild(i);
                    if (c != _label.transform && c != _line.transform && (_frame == null || c != _frame.transform)) return true;
                }
                return false;
            }
        }

        private enum Kind { Primary, Secondary, Selected }
        private Kind _kind;

        public void Apply(Color background, Color text)
        {
            _background = background; _text = text;
            if (_image == null || _label == null) return;
            if (IsCard) { if (_frame != null) _frame.gameObject.SetActive(false); _image.color = background; _label.color = UiKit.Readable(background, text); return; }
            // 투명 바탕·글자 없는 버튼은 위에 겹친 클릭 판정 영역이므로 투명하게 둔다
            _hitArea = background.a < 0.01f || string.IsNullOrEmpty(_label.text);
            if (_hitArea) { _image.color = Color.clear; if (_frame != null) _frame.gameObject.SetActive(false); return; }
            _image.sprite = UiKit.PixelBevel; _image.type = Image.Type.Sliced; _image.pixelsPerUnitMultiplier = UiKit.PixelScale;
            float lum = background.r * 0.299f + background.g * 0.587f + background.b * 0.114f;
            bool reddish = background.r > background.g * 1.35f && background.r > 0.25f && lum < 0.45f;
            _kind = background.a > 0.5f && lum > 0.55f ? Kind.Primary : reddish ? Kind.Selected : Kind.Secondary;
            bool disabled = _button != null && !_button.interactable;
            _label.color = disabled ? Palette.DisabledText : _kind == Kind.Primary ? Palette.OnAccent : _kind == Kind.Selected ? Palette.MaroonText : (text.r * 0.299f + text.g * 0.587f + text.b * 0.114f < 0.35f ? Palette.Text : text);
            UiKit.SetStyle(_label, _kind != Kind.Secondary || disabled ? FontStyle.Bold : FontStyle.Normal);
            if (_frame != null) _frame.gameObject.SetActive(true);
            Refresh();
        }

        // 바탕색
        public static readonly Color Fill = new Color32(0x2A, 0x1D, 0x12, 0xFF);           // 보조 버튼(어두운 갈색)
        public static readonly Color FillHover = new Color32(0x3B, 0x2A, 0x1A, 0xFF);
        public static readonly Color FillPrimary = new Color32(0xD9, 0xC7, 0xA0, 0xFF);    // 주 버튼(베이지)
        public static readonly Color FillPrimaryHover = new Color32(0xEB, 0xDC, 0xB8, 0xFF);
        public static readonly Color FillSelected = new Color32(0x4A, 0x16, 0x22, 0xFF);   // 선택된 탭(자주)
        public static readonly Color FillSelectedHover = new Color32(0x5E, 0x1D, 0x2C, 0xFF);
        public static readonly Color FillDisabled = new Color32(0x1E, 0x15, 0x0E, 0xFF);
        // 외곽선색
        private static readonly Color FramePrimary = new Color32(0x5A, 0x43, 0x26, 0xFF);
        private static readonly Color FrameSecondary = new Color32(0x6B, 0x4F, 0x2C, 0xFF);
        private static readonly Color FrameSelected = new Color32(0x7A, 0x28, 0x38, 0xFF);
        private static readonly Color FrameDisabled = new Color32(0x3E, 0x2E, 0x1C, 0xFF);
        private bool _hitArea;

        private bool _lastInteractable = true;
        private void Start() { Apply(_background, _text); _lastInteractable = _button == null || _button.interactable; } // 자식(로고 등)이 다 붙은 뒤 카드형 여부 다시 판단
        private void Update()
        {
            // interactable을 SetButton 없이 바꾼 경우에도 글자색(비활성)을 맞춘다
            bool now = _button == null || _button.interactable;
            if (now == _lastInteractable) return;
            _lastInteractable = now; Apply(_background, _text);
        }
        private void OnDisable() { _hover = false; if (_pressed) SetPressed(false); Refresh(); }
        public void OnPointerEnter(PointerEventData e) { _hover = true; Refresh(); }
        public void OnPointerExit(PointerEventData e) { _hover = false; Refresh(); }

        private void Refresh()
        {
            if (_image == null || _label == null || IsCard || _hitArea) return;
            bool disabled = _button != null && !_button.interactable;
            if (disabled) { _image.color = FillDisabled; if (_frame != null) _frame.color = FrameDisabled; return; }
            switch (_kind)
            {
                case Kind.Primary: _image.color = _hover ? FillPrimaryHover : FillPrimary; if (_frame != null) _frame.color = FramePrimary; break;
                case Kind.Selected: _image.color = _hover ? FillSelectedHover : FillSelected; if (_frame != null) _frame.color = FrameSelected; break;
                default: _image.color = _hover ? FillHover : Fill; if (_frame != null) _frame.color = _hover ? Palette.Accent : FrameSecondary; break;
            }
            if (_pressed) _image.color = Color.Lerp(_image.color, Color.black, 0.25f); // 눌린 동안 어둡게
        }
    }
    /// <summary>코인을 글자뿐 아니라 금화 그림으로도 보여 주는 작은 묶음(아이콘 + "N개").</summary>
    public sealed class CoinBadge
    {
        public readonly RectTransform Root;
        public readonly Image Icon;
        public readonly Text Count;

        public CoinBadge(Transform parent, int fontSize)
        {
            Root = UiKit.Rect(parent, "CoinBadge");
            Icon = UiKit.Panel(Root, "CoinIcon", Color.white);
            Icon.sprite = UiKit.Coin;
            Icon.preserveAspect = true;
            // 아이콘: 왼쪽, 높이만큼 정사각형
            Icon.rectTransform.anchorMin = new Vector2(0, 0.1f); Icon.rectTransform.anchorMax = new Vector2(0, 0.9f);
            Icon.rectTransform.pivot = new Vector2(0, 0.5f);
            Icon.rectTransform.offsetMin = Vector2.zero; Icon.rectTransform.offsetMax = new Vector2(fontSize + 8, 0);
            Count = UiKit.Label(Root, "", fontSize, Palette.Text, TextAnchor.MiddleLeft);
            Count.name = "CoinCount";
            Count.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Stretch(Count.rectTransform, fontSize + 14, 0, 0, 0);
        }

        public void Set(int coins) => Count.text = "<b>" + coins + "</b>개";
        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);
    }
}
