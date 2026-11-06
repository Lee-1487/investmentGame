// NEXT-D01 일봉(캔들) 그래프(uGUI Graphic). 실제 주식 차트처럼 하루 = 봉 1개.
// - 세로축 = 0일차 대비 %. 몸통은 시가(전날 종가)→종가(그날 값) 사이에 뜬다(0% 기준선에 붙지 않음).
// - 양봉(종가>시가) 빨강, 음봉(종가<시가) 파랑, 보합은 가는 가로선. 색과 함께 툴팁의 '양봉/음봉' 문구로도 구분된다.
// - 하루 값이 1개뿐이라 고가·저가가 없으므로 꼬리는 그리지 않는다(값을 지어내지 않음).
// - 전날이 미열람이면 시가를 모르므로 종가 위치에 회색 표식만 그린다. 미열람 날짜는 칸 전체 음영(값 없음).
// - 날짜 좌표는 DayAxis(31등분)를 쓰며, 같은 규칙으로 클릭 날짜를 계산한다(ChartPointerLayer).
// - 선택 구간·진입일 후보·미리보기·마우스 위치 강조도 날짜 기준으로 그린다(값이 없는 날짜도 가능).
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BarChartGraphic : MaskableGraphic
    {
        public static readonly Color32 UpColor = new Color32(0xDC, 0x26, 0x26, 0xFF);
        public static readonly Color32 DownColor = new Color32(0x25, 0x63, 0xEB, 0xFF);
        public static readonly Color32 ZeroColor = new Color32(0x37, 0x41, 0x51, 0xFF);
        public static readonly Color32 FlatColor = new Color32(0x37, 0x41, 0x51, 0xFF);
        public static readonly Color32 UnknownOpenColor = new Color32(0x6B, 0x72, 0x80, 0xFF);
        public static readonly Color32 SubGridColor = new Color32(0xE5, 0xE7, 0xEB, 0xFF);
        public static readonly Color32 HiddenColor = new Color32(0x9C, 0xA3, 0xAF, 0x38);
        public static readonly Color32 GridColor = new Color32(0xCB, 0xD0, 0xD8, 0xFF);
        public static readonly Color32 BandColor = new Color32(0x25, 0x63, 0xEB, 0x2E);
        public static readonly Color32 BandEdgeColor = new Color32(0x25, 0x63, 0xEB, 0xCC);
        public static readonly Color32 CandidateColor = new Color32(0xF5, 0x9E, 0x0B, 0xFF);
        public static readonly Color32 PreviewOkColor = new Color32(0x05, 0x96, 0x69, 0x30);
        public static readonly Color32 PreviewBadColor = new Color32(0xDC, 0x26, 0x26, 0x26);
        public static readonly Color32 AllowedColor = new Color32(0x05, 0x96, 0x69, 0x1F);
        public static readonly Color32 AllowedEdgeColor = new Color32(0x05, 0x96, 0x69, 0x8C);
        public static readonly Color32 HoverColor = new Color32(0x11, 0x18, 0x27, 0x22);

        private DayBar[] _bars = new DayBar[0];
        private int _limitBps = 1;
        private readonly List<Vector2Int> _bands = new List<Vector2Int>();
        private int? _candidate;
        private Vector2Int? _preview;
        private bool _previewOk;
        private int? _hover;
        private Vector2Int? _allowed;
        private RectTransform _tradeMarkerRoot;
        private string _tradeMarkerKey;

        protected override void Awake()
        {
            base.Awake();
            // 입력이 필요하면 ChartPointerLayer가 켠다. 이미 붙어 있으면 끄지 않는다(AddComponent 순서 무관).
            raycastTarget = GetComponent<ChartPointerLayer>() != null;
        }

        public void SetInteractive(bool on) => raycastTarget = on;

        public void SetData(DayBar[] bars, int limitBps)
        {
            _bars = bars ?? new DayBar[0];
            _limitBps = Mathf.Max(1, limitBps);
            SetVerticesDirty();
        }

        public void SetBands(IEnumerable<Vector2Int> bands)
        {
            _bands.Clear();
            if (bands != null) _bands.AddRange(bands);
            SetVerticesDirty();
        }

        public void SetTradeIntervals(IEnumerable<Interval> intervals, bool planned = false)
        {
            var ranges = (intervals ?? new Interval[0]).Where(i => i != null).ToArray();
            SetBands(ranges.Select(i => new Vector2Int(i.entryDay, i.exitDay)));
            string key = planned + ":" + string.Join(";", ranges.Select(i => i.entryDay + "," + i.exitDay));
            if (key == _tradeMarkerKey) return;
            _tradeMarkerKey = key;
            if (_tradeMarkerRoot == null)
            { _tradeMarkerRoot = UiKit.Rect(transform, "TradeMarkers"); UiKit.Stretch(_tradeMarkerRoot); }
            UiKit.Clear(_tradeMarkerRoot);
            for (int i = 0; i < ranges.Length; i++)
            {
                TradeMarker("EntryMarker_" + i, "진입 " + ranges[i].entryDay, ranges[i].entryDay, i, Palette.Success);
                TradeMarker("ExitMarker_" + i, "청산 " + ranges[i].exitDay, ranges[i].exitDay, i, Palette.Accent);
            }
        }
        private void TradeMarker(string name, string text, int day, int row, Color color)
        {
            var label = UiKit.Label(_tradeMarkerRoot, text, 9, color, TextAnchor.UpperCenter, FontStyle.Bold);
            label.name = name;
            UiKit.Place(label.rectTransform, new Vector2((day + .5f) / DayAxis.Count, 1), new Vector2(.5f, 1),
                new Vector2(0, -row * 13), new Vector2(56, 13));
        }

        public void SetCandidate(int? day)
        {
            _candidate = day;
            SetVerticesDirty();
        }

        public void SetPreview(Vector2Int? range, bool ok)
        {
            _preview = range;
            _previewOk = ok;
            SetVerticesDirty();
        }

        /// <summary>지금 클릭할 수 있는 날짜 범위(포함)를 옅은 초록으로 표시. 미열람 날짜도 포함된다.</summary>
        public void SetAllowed(Vector2Int? range)
        {
            if (_allowed == range) return;
            _allowed = range;
            SetVerticesDirty();
        }

        public void SetHover(int? day)
        {
            if (_hover == day) return;
            _hover = day;
            SetVerticesDirty();
        }

        /// <summary>이 그래프의 로컬 x → 날짜(그리기와 같은 좌표).</summary>
        public int? DayAtLocalX(float localX)
        {
            var r = rectTransform.rect;
            return DayAxis.DayAt(localX, r.xMin, r.width);
        }

        private float ValueY(Rect r, int bps)
        {
            float t = Mathf.Clamp(bps, -_limitBps, _limitBps) / (float)_limitBps; // -1..1
            return r.center.y + t * (r.height * 0.5f);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect; // DayAxis와 같은 사각형(클릭 판정과 일치)
            if (r.width <= 0 || r.height <= 0) return;
            float slot = DayAxis.SlotWidth(r.width);
            float zeroY = r.center.y;

            // 미열람 날짜: 칸 전체 음영
            for (int i = 0; i < _bars.Length; i++)
            {
                if (_bars[i].State != DayBarState.Hidden) continue;
                float x0 = DayAxis.LeftX(_bars[i].Day, r.xMin, r.width);
                Quad(vh, x0 + 0.5f, r.yMin, x0 + slot - 0.5f, r.yMax, HiddenColor);
            }

            // 클릭 가능한 날짜(진입 가능 범위 또는 청산 가능 범위). 미열람 음영 위에 겹쳐 그린다.
            if (_allowed.HasValue)
            {
                int a = Mathf.Clamp(_allowed.Value.x, 0, DayAxis.Count - 1);
                int b = Mathf.Clamp(_allowed.Value.y, 0, DayAxis.Count - 1);
                if (b >= a)
                {
                    float ax = DayAxis.LeftX(a, r.xMin, r.width);
                    float bx = DayAxis.LeftX(b, r.xMin, r.width) + slot;
                    Quad(vh, ax, r.yMin, bx, r.yMax, AllowedColor);
                    Quad(vh, ax, r.yMin, bx, r.yMin + 2f, AllowedEdgeColor);
                }
            }

            // 선택 구간과 미리보기(진입일 칸 중앙 → 청산일 칸 중앙)
            foreach (var b in _bands) Band(vh, r, b.x, b.y, BandColor, true);
            if (_preview.HasValue) Band(vh, r, _preview.Value.x, _preview.Value.y, _previewOk ? PreviewOkColor : PreviewBadColor, false);

            // 마우스 위치
            if (_hover.HasValue && _hover.Value >= 0 && _hover.Value < DayAxis.Count)
            {
                float x0 = DayAxis.LeftX(_hover.Value, r.xMin, r.width);
                Quad(vh, x0, r.yMin, x0 + slot, r.yMax, HoverColor);
            }

            // 보조 가로선(±한도의 절반·한도)과 0% 기준선
            foreach (float f in new[] { -1f, -0.5f, 0.5f, 1f })
            {
                float gy = zeroY + f * (r.height * 0.5f);
                gy = Mathf.Clamp(gy, r.yMin + 0.5f, r.yMax - 0.5f);
                Quad(vh, r.xMin, gy - 0.5f, r.xMax, gy + 0.5f, SubGridColor);
            }
            Quad(vh, r.xMin, zeroY - 0.5f, r.xMax, zeroY + 0.5f, GridColor);

            // 일봉
            float bodyW = Mathf.Max(1f, slot * 0.62f);
            for (int i = 0; i < _bars.Length; i++)
            {
                var bar = _bars[i];
                if (!bar.Bps.HasValue) continue;
                float cx = DayAxis.CenterX(bar.Day, r.xMin, r.width);
                float closeY = ValueY(r, bar.Bps.Value);
                byte alpha = bar.Source == DailyBarModel.RevealedSource ? (byte)0x8C : (byte)0xFF;

                if (bar.Move == CandleMove.Unknown || !bar.OpenBps.HasValue)
                {
                    // 시가 모름: 종가 위치에 짧은 회색 표식(몸통 없음)
                    var c = UnknownOpenColor; c.a = alpha;
                    float w = bodyW * 0.7f;
                    Quad(vh, cx - w * 0.5f, closeY - 1.5f, cx + w * 0.5f, closeY + 1.5f, c);
                    continue;
                }

                if (bar.Move == CandleMove.Flat)
                {
                    var c = FlatColor; c.a = alpha;
                    Quad(vh, cx - bodyW * 0.5f, closeY - 1f, cx + bodyW * 0.5f, closeY + 1f, c);
                    continue;
                }

                float openY = ValueY(r, bar.OpenBps.Value);
                var color = bar.Move == CandleMove.Rise ? UpColor : DownColor;
                color.a = alpha;
                float y0 = Mathf.Min(openY, closeY), y1 = Mathf.Max(openY, closeY);
                if (y1 - y0 < 2f) { float m = (y0 + y1) * 0.5f; y0 = m - 1f; y1 = m + 1f; } // 너무 얇은 몸통도 보이게
                Quad(vh, cx - bodyW * 0.5f, y0, cx + bodyW * 0.5f, y1, color);
            }

            // 진입일 후보 표식
            if (_candidate.HasValue && _candidate.Value >= 0 && _candidate.Value < DayAxis.Count)
            {
                float cx = DayAxis.CenterX(_candidate.Value, r.xMin, r.width);
                Quad(vh, cx - 1.5f, r.yMin, cx + 1.5f, r.yMax, CandidateColor);
            }
        }

        private static void Band(VertexHelper vh, Rect r, int entry, int exit, Color32 color, bool edges)
        {
            int a = Mathf.Clamp(Mathf.Min(entry, exit), 0, DayAxis.Count - 1);
            int b = Mathf.Clamp(Mathf.Max(entry, exit), 0, DayAxis.Count - 1);
            float x0 = DayAxis.CenterX(a, r.xMin, r.width);
            float x1 = DayAxis.CenterX(b, r.xMin, r.width);
            Quad(vh, x0, r.yMin, x1, r.yMax, color);
            if (!edges) return;
            Quad(vh, x0 - 1f, r.yMin, x0 + 1f, r.yMax, BandEdgeColor);
            Quad(vh, x1 - 1f, r.yMin, x1 + 1f, r.yMax, BandEdgeColor);
        }

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 color)
        {
            int i = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = color;
            v.position = new Vector3(x0, y0); vh.AddVert(v);
            v.position = new Vector3(x0, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y1); vh.AddVert(v);
            v.position = new Vector3(x1, y0); vh.AddVert(v);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
