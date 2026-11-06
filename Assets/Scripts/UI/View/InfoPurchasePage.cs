// 정보 구매 창: 기업 카드 6개(위 3개·아래 3개, REQ-005).
// 카드마다 일별 그래프(열람한 날짜만, 숨은 구간은 잇지 않고 음영)와 묶음 6개 버튼.
// 코인·열람 상태는 서버 응답으로만 바뀐다. 버튼은 요청 의도만 보낸다.
using System.Collections.Generic;
using System;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public static class CompanyInfo
    {
        public static string Volatility(string companyId)
        {
            switch (companyId)
            {
                case "AI":
                case "SPACE": return "변동성 높음";
                case "FOOD":
                case "BANK": return "변동성 낮음";
                case "BIO": return "변동성 극단적";
                case "AUTO": return "변동성 중간";
                default: return "";
            }
        }

        public static string BundleLabel(int bundleIndex)
            => GraphSeriesBuilder.BundleStartDay(bundleIndex) + "~" + GraphSeriesBuilder.BundleEndDay(bundleIndex) + "일";

        public static void RefreshPurchaseButton(Button button, GameClientController controller, string companyId, int bundleIndex, bool detailed = false)
        {
            var store = controller.Store;
            var company = store.View?.companies?.FirstOrDefault(c => c.companyId == companyId);
            string label = BundleLabel(bundleIndex);
            if (GraphSeriesBuilder.TryGetSource(company, bundleIndex, out var source))
            {
                bool purchased = source == BundleSources.Purchased;
                UiKit.SetButton(button, label + (detailed ? (purchased ? " · 구매함" : " · 열람 중") : ""), false,
                    purchased ? Palette.AccentSoft : Palette.Disabled, purchased ? Palette.Accent : Palette.SubText);
            }
            else if (store.IsPurchasePending(companyId, bundleIndex))
                UiKit.SetButton(button, label + (detailed ? " · 요청 중" : ""), false, Palette.AccentSoft, Palette.Accent);
            else
            {
                bool free = store.View?.ability?.freeInformationCredits > 0;
                bool can = store.Public?.phase == Protocol.Info && !store.Public.paused && string.IsNullOrEmpty(store.EndReason)
                    && ((store.View?.coins ?? 0) > 0 || free);
                UiKit.SetButton(button, label + (detailed ? (free ? " · 무료" : " · 1코인") : ""), can,
                    can ? Palette.Accent : Palette.Disabled, can ? Color.white : Palette.DisabledText);
            }
        }
    }

    public sealed class InfoPurchasePage
    {
        private readonly GameClientController _c;
        private readonly Text _header;
        private readonly List<CompanyCard> _cards = new List<CompanyCard>();
        private readonly RectTransform _grid, _zoom, _planRows;
        private readonly BarChartGraphic _zoomChart;
        private readonly Text _zoomTitle, _zoomTip, _zoomTop, _zoomBottom, _zoomMessage;
        private readonly Button _favorite, _cancelEntry;
        private readonly Button[] _rankButtons = new Button[7];
        private readonly Button[] _zoomBundleButtons = new Button[UiRules.BundleCount];
        private readonly Text _zoomPurchaseStatus;
        private string _zoomCompany, _zoomPhase, _rowKey, _note = "";
        private int _zoomTurn;
        private int? _entryDay, _hoverDay;
        private DayBar[] _zoomBars = new DayBar[0];

        public InfoPurchasePage(RectTransform page, GameClientController controller, RectTransform overlayParent = null)
        {
            _c = controller;

            var title = UiKit.Label(page, "정보 구매 창", 18, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -2), new Vector2(300, 28));

            _header = UiKit.Label(page, "", 14, Palette.SubText, TextAnchor.MiddleRight);
            UiKit.Place(_header.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-6, -2), new Vector2(600, 28));

            var grid = _grid = UiKit.Rect(page, "CompanyGrid");
            UiKit.Stretch(grid, 0, 34, 0, 0);

            var ids = UiRules.CompanyIds;
            for (int i = 0; i < ids.Length; i++)
            {
                int col = i % 3;
                int row = i / 3; // 0 = 위, 1 = 아래
                var cell = UiKit.Rect(grid, "Card_" + ids[i]);
                UiKit.Anchor(cell, col / 3f, row == 0 ? 0.5f : 0f, (col + 1) / 3f, row == 0 ? 1f : 0.5f, 5, 5);
                _cards.Add(new CompanyCard(cell, ids[i], controller, Enlarge));
            }

            _zoom = UiKit.Panel(overlayParent ?? page, "ZoomCompanyPanel", Palette.Screen, rounded: true, raycast: true).rectTransform;
            UiKit.Stretch(_zoom, 4, 4, 4, 4);
            _zoomTitle = UiKit.Label(_zoom, "", 22, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(_zoomTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -12), new Vector2(850, 36));
            var shrink = UiKit.Button(_zoom, "축소하기", 14, Palette.Card, Palette.Text, Collapse);
            shrink.name = "ShrinkChart";
            UiKit.Place((RectTransform)shrink.transform, Vector2.one, Vector2.one, new Vector2(-16, -12), new Vector2(110, 34));
            _zoomTip = UiKit.Label(_zoom, "", 13, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(_zoomTip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -50), new Vector2(980, 30));
            var chart = UiKit.Rect(_zoom, "ZoomChart"); UiKit.Stretch(chart, 64, 88, 28, 228);
            _zoomChart = chart.gameObject.AddComponent<BarChartGraphic>();
            var pointer = chart.gameObject.AddComponent<ChartPointerLayer>();
            pointer.Clicked = PlanDay;
            pointer.Hovered = day => { _hoverDay = day; RefreshZoomHover(); };
            foreach (int day in new[] { 0, 5, 10, 15, 20, 25, 30 })
            {
                var tick = UiKit.Label(chart, day + "일", 11, Palette.SubText, TextAnchor.UpperCenter);
                UiKit.Place(tick.rectTransform, new Vector2((day + .5f) / DayAxis.Count, 0), new Vector2(.5f, 1), new Vector2(0, -2), new Vector2(44, 20));
            }
            _zoomTop = UiKit.Label(_zoom, "", 12, Palette.SubText, TextAnchor.UpperRight);
            UiKit.Place(_zoomTop.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -88), new Vector2(58, 22));
            _zoomBottom = UiKit.Label(_zoom, "", 12, Palette.SubText, TextAnchor.LowerRight);
            UiKit.Place(_zoomBottom.rectTransform, Vector2.zero, Vector2.zero, new Vector2(0, 228), new Vector2(58, 22));
            _favorite = UiKit.Button(_zoom, "찜하기", 13, Palette.Card, Palette.Text,
                () => { if (CanPlan) _c.Store.TradePlans.SetFavorite(_zoomCompany, !_c.Store.TradePlans.Get(_zoomCompany).Favorite); });
            _favorite.name = "FavoriteCompany";
            UiKit.Place((RectTransform)_favorite.transform, Vector2.zero, Vector2.zero, new Vector2(20, 172), new Vector2(120, 32));
            var rankLabel = UiKit.Label(_zoom, "우선순위", 13, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(rankLabel.rectTransform, Vector2.zero, Vector2.zero, new Vector2(156, 172), new Vector2(64, 32));
            for (int i = 0; i < _rankButtons.Length; i++)
            {
                int rank = i;
                var button = UiKit.Button(_zoom, i == 0 ? "미정" : i + "순위", 12, Palette.Card, Palette.Text,
                    () => { if (CanPlan) _c.Store.TradePlans.SetRank(_zoomCompany, rank); });
                button.name = "PlanRank_" + rank;
                UiKit.Place((RectTransform)button.transform, Vector2.zero, Vector2.zero, new Vector2(224 + i * 66, 172), new Vector2(60, 32));
                _rankButtons[i] = button;
            }
            _cancelEntry = UiKit.Button(_zoom, "진입 후보 취소", 12, Palette.Card, Palette.Text,
                () => { _entryDay = null; _note = "진입 후보를 취소했습니다."; RefreshZoom(); });
            _cancelEntry.name = "CancelPlanEntry";
            UiKit.Place((RectTransform)_cancelEntry.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-20, 172), new Vector2(140, 32));
            _planRows = UiKit.Rect(_zoom, "PlannedIntervals");
            _planRows.anchorMin = Vector2.zero; _planRows.anchorMax = new Vector2(1, 0);
            _planRows.offsetMin = new Vector2(20, 92); _planRows.offsetMax = new Vector2(-20, 164);
            _zoomMessage = UiKit.Label(_zoom, "", 12, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(_zoomMessage.rectTransform, Vector2.zero, Vector2.zero, new Vector2(20, 54), new Vector2(1080, 32));
            _zoomPurchaseStatus = UiKit.Label(_zoom, "", 13, Palette.SubText, TextAnchor.MiddleLeft);
            _zoomPurchaseStatus.name = "ZoomPurchaseStatus";
            UiKit.Place(_zoomPurchaseStatus.rectTransform, Vector2.zero, Vector2.zero, new Vector2(20, 10), new Vector2(260, 36));
            var purchaseRow = UiKit.Rect(_zoom, "ZoomInformationBundles");
            purchaseRow.anchorMin = Vector2.zero; purchaseRow.anchorMax = new Vector2(1, 0);
            purchaseRow.offsetMin = new Vector2(286, 10); purchaseRow.offsetMax = new Vector2(-20, 46);
            for (int i = 0; i < _zoomBundleButtons.Length; i++)
            {
                int bundle = i;
                var button = UiKit.Button(purchaseRow, "", 11, Palette.Accent, Color.white, () => _c.BuyInformation(_zoomCompany, bundle));
                button.name = "ZoomBuyBundle_" + i;
                UiKit.Anchor((RectTransform)button.transform, i / 6f, 0, (i + 1) / 6f, 1, 3, 0);
                _zoomBundleButtons[i] = button;
            }
            _zoom.gameObject.SetActive(false);
        }

        private bool CanPlan => _zoomCompany != null && string.IsNullOrEmpty(_c.Store.EndReason) && !_c.Store.OrderLocked && _c.Store.Public?.paused != true
            && (_c.Store.Public?.phase == Protocol.Info || _c.Store.Public?.phase == Protocol.Selection);
        private void Enlarge(string company)
        {
            _zoomCompany = company; _zoomPhase = _c.Store.Public?.phase; _zoomTurn = _c.Store.CurrentTurn;
            _entryDay = _hoverDay = null; _note = ""; _rowKey = null;
            _grid.gameObject.SetActive(false); _zoom.gameObject.SetActive(true); _zoom.SetAsLastSibling();
            _zoomChart.name = "ZoomChart_" + company; RefreshZoom();
        }
        public void Collapse()
        { _zoomCompany = null; _entryDay = _hoverDay = null; _grid.gameObject.SetActive(true); _zoom.gameObject.SetActive(false); }
        private void PlanDay(int day, bool right)
        {
            if (!CanPlan) return;
            if (right) { _entryDay = null; _note = "진입 후보를 취소했습니다."; RefreshZoom(); return; }
            var plan = _c.Store.TradePlans.Get(_zoomCompany);
            if (!_entryDay.HasValue)
            {
                if (plan.Intervals.Length >= UiRules.MaxIntervalsPerCompany) _note = "최대 2구간입니다. 기존 구간을 삭제한 뒤 다시 선택하세요.";
                else if (day > UiRules.MaxDay - UiRules.MinHoldingDays) _note = "진입일은 0~25일 중에서 선택하세요.";
                else { _entryDay = day; _note = day + "일 진입 후보 · 5~10일 뒤의 청산일을 클릭하세요."; }
            }
            else
            {
                string error = _c.Store.TradePlans.TryAddInterval(_zoomCompany, _entryDay.Value, day);
                if (error.Length > 0) _note = ErrorMessages.ToKorean(error);
                else { _entryDay = null; _note = "개인 계획을 저장했습니다. 선택한 기업의 투자 초안에 적용됩니다."; }
            }
            RefreshZoom();
        }
        private void RefreshZoom()
        {
            if (_zoomCompany == null) return;
            var store = _c.Store; var plan = store.TradePlans.Get(_zoomCompany);
            var company = store.View?.companies?.FirstOrDefault(c => c.companyId == _zoomCompany);
            var revealed = store.RevealedTurn == store.CurrentTurn ? store.Revealed?.FirstOrDefault(c => c.companyId == _zoomCompany) : null;
            _zoomBars = DailyBarModel.Build(company, revealed);
            int cap = GraphSeriesBuilder.AxisLimitBps(_zoomCompany);
            _zoomChart.SetData(_zoomBars, cap);
            var intervals = store.TradePlans.ChartIntervals(store.View, store.CurrentTurn, _zoomCompany, store.OrderLocked);
            _zoomChart.SetTradeIntervals(intervals, !store.OrderLocked);
            _zoomChart.SetCandidate(_entryDay);
            Vector2Int? allowed = null;
            if (CanPlan && (_entryDay.HasValue || plan.Intervals.Length < UiRules.MaxIntervalsPerCompany))
                allowed = _entryDay.HasValue ? new Vector2Int(_entryDay.Value + 5, Math.Min(30, _entryDay.Value + 10)) : new Vector2Int(0, 25);
            _zoomChart.SetAllowed(allowed);
            _zoomTop.text = DisplayFormat.Percent(cap); _zoomBottom.text = DisplayFormat.Percent(-cap);
            _zoomTitle.text = CompanyCatalog.Get(_zoomCompany).DisplayName + " · " + _zoomCompany + "  <size=13>"
                + (store.OrderLocked ? "확정 주문 진입·청산" : "이번 턴 개인 투자 계획") + "</size>";
            _zoomPurchaseStatus.text = store.Public?.phase == Protocol.Info
                ? "정보 구매 · 보유 " + (store.View?.coins ?? 0) + "코인" + (store.View?.ability?.freeInformationCredits > 0 ? " · 무료 " + store.View.ability.freeInformationCredits + "회" : "")
                : "정보 구매는 정보 탐색 단계에서만";
            for (int i = 0; i < _zoomBundleButtons.Length; i++) CompanyInfo.RefreshPurchaseButton(_zoomBundleButtons[i], _c, _zoomCompany, i, true);
            UiKit.SetButton(_favorite, plan.Favorite ? "★ 찜 해제" : "☆ 찜하기", CanPlan, plan.Favorite ? Palette.AccentSoft : Palette.Card, Palette.Accent);
            for (int i = 0; i < _rankButtons.Length; i++)
                UiKit.SetButton(_rankButtons[i], i == 0 ? "미정" : i + "순위", CanPlan, plan.Rank == i ? Palette.Accent : Palette.Card, plan.Rank == i ? Color.white : Palette.Text);
            _cancelEntry.interactable = CanPlan && _entryDay.HasValue;
            string rowsKey = _zoomCompany + ":" + store.OrderLocked + ":" + CanPlan + ":" + string.Join(";", intervals.Select(i => i.entryDay + "," + i.exitDay));
            if (_rowKey != rowsKey)
            {
                _rowKey = rowsKey; UiKit.Clear(_planRows);
                if (intervals.Length == 0)
                { var empty = UiKit.Label(_planRows, store.OrderLocked ? "이 기업에는 확정된 주문이 없습니다." : "그래프에서 진입일 → 청산일 순서로 클릭하세요. 보유 5~10일 · 최대 2구간", 14, Palette.SubText); UiKit.Stretch(empty.rectTransform); }
                for (int i = 0; i < intervals.Length; i++)
                {
                    int index = i; var interval = intervals[i];
                    var row = ScreenKit.TopStrip(_planRows, "Plan_" + i, i * 34, 30);
                    var label = UiKit.Label(row, (store.OrderLocked ? "확정 " : "계획 ") + (i + 1) + " · 진입 " + interval.entryDay + "일 → 청산 " + interval.exitDay + "일 (" + (interval.exitDay - interval.entryDay) + "일 보유)", 14, Palette.Text, TextAnchor.MiddleLeft);
                    UiKit.Stretch(label.rectTransform, 0, 0, 90, 0);
                    if (CanPlan)
                    {
                        var remove = UiKit.Button(row, "삭제", 12, Palette.Card, Palette.SubText, () => { if (CanPlan) store.TradePlans.RemoveInterval(_zoomCompany, index); });
                        remove.name = "RemovePlan_" + i;
                        UiKit.Place((RectTransform)remove.transform, new Vector2(1, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(76, 28));
                    }
                }
            }
            _zoomMessage.text = !CanPlan ? (store.OrderLocked ? "확정 주문은 바꿀 수 없습니다. 진입·청산 표시는 본인의 주문만 보여 줍니다."
                : "계획은 정보 탐색·기업 선택 단계에서 편집합니다. 투자 단계에서는 투자 화면에서 구간을 변경하고 주문을 확정하세요.")
                : _note.Length > 0 ? _note : "이번 턴에만 저장되는 개인 계획입니다. 같은 우선순위를 고르면 기존 순위와 교환합니다. 주문은 투자 화면에서 따로 확정하세요.";
            RefreshZoomHover();
        }
        private void RefreshZoomHover()
        {
            _zoomChart.SetPreview(null, false);
            if (!_hoverDay.HasValue || _hoverDay.Value >= _zoomBars.Length) { _zoomTip.text = "미열람 구간은 주가를 표시하지 않습니다."; return; }
            int day = _hoverDay.Value; _zoomTip.text = DailyBarModel.Tooltip(_zoomBars[day]);
            if (CanPlan && _entryDay.HasValue && day > _entryDay.Value)
            {
                var intervals = _c.Store.TradePlans.Get(_zoomCompany).Intervals.Concat(new[] { new Interval { entryDay = _entryDay.Value, exitDay = day } }).ToArray();
                _zoomChart.SetPreview(new Vector2Int(_entryDay.Value, day), OrderInputValidator.ValidateIntervals(intervals).Length == 0);
                int? ret = DailyBarModel.IntervalReturnBps(_zoomBars, _entryDay.Value, day);
                _zoomTip.text += " · 구간 " + (ret.HasValue ? DisplayFormat.Percent(ret.Value) : "수익률 미확인");
            }
        }

        public void Refresh()
        {
            var v = _c.Store.View;
            var phase = _c.Store.Public?.phase;
            _header.text = v == null
                ? "개인 정보 수신 대기"
                : "보유 코인 <b>" + v.coins + "개</b>   ·   자산 " + DisplayFormat.Money(v.assets)
                  + (phase == Protocol.Info ? "   ·   1코인 = 정보 1묶음" : "   ·   구매는 정보 탐색 단계에서만");
            foreach (var card in _cards) card.Refresh();
            if (_zoomCompany != null)
            {
                if (_zoomTurn != _c.Store.CurrentTurn || _zoomPhase != phase) Collapse();
                else RefreshZoom();
            }
        }
    }

    internal sealed class CompanyCard
    {
        private readonly string _companyId;
        private readonly GameClientController _c;
        private readonly Text _seats;
        private readonly BarChartGraphic _chart;
        private readonly Text _tip;
        private readonly Text _planLabel;
        private readonly Button _expand;
        private DayBar[] _bars = new DayBar[0];
        private readonly Text _axisTop, _axisBottom;
        private readonly Button[] _bundleButtons = new Button[UiRules.BundleCount];

        public CompanyCard(RectTransform cell, string companyId, GameClientController controller, Action<string> enlarge = null)
        {
            _companyId = companyId;
            _c = controller;

            var bg = UiKit.Panel(cell, "Bg", Palette.CardBorder, rounded: true);
            UiKit.Stretch(bg.rectTransform);
            var inner = UiKit.Panel(bg.transform, "Inner", Palette.Card, rounded: true);
            UiKit.Stretch(inner.rectTransform, 1, 1, 1, 1);
            var root = inner.rectTransform;

            // NEXT-D03 기업 이미지(없으면 배지)와 표시명. 통신 ID는 그대로.
            var entry = CompanyCatalog.Get(companyId);
            var logo = CompanyBadge.Create(root, companyId, 24);
            UiKit.Place(logo, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -5), new Vector2(24, 24));
            var name = UiKit.Label(root, "<b>" + entry.DisplayName + "</b> <size=10>" + entry.Id + " · " + entry.Volatility + "</size>",
                14, Palette.Text, TextAnchor.UpperLeft);
            UiKit.Stretch(name.rectTransform, 38, 8, 84, 0);

            _expand = UiKit.Button(root, "확대하기", 11, Palette.AccentSoft, Palette.Accent, () => enlarge?.Invoke(_companyId));
            _expand.name = "Expand_" + companyId;
            UiKit.Place((RectTransform)_expand.transform, Vector2.one, Vector2.one, new Vector2(-6, -5), new Vector2(70, 24));

            _seats = UiKit.Label(root, "", 12, Palette.SubText, TextAnchor.UpperRight);
            UiKit.Place(_seats.rectTransform, Vector2.one, Vector2.one, new Vector2(-8, -31), new Vector2(90, 16));

            // 그래프 영역
            var chartRt = UiKit.Rect(root, "Chart");
            UiKit.Stretch(chartRt, 40, 54, 10, 82);
            _chart = chartRt.gameObject.AddComponent<BarChartGraphic>();
            var pointer = chartRt.gameObject.AddComponent<ChartPointerLayer>(); // 마우스 위치 날짜의 값 툴팁
            pointer.Hovered = OnHover;

            _tip = UiKit.Label(root, "", 10, Palette.Text, TextAnchor.MiddleRight);
            UiKit.Place(_tip.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-8, -40), new Vector2(220, 14));

            _axisTop = UiKit.Label(root, "", 10, Palette.SubText, TextAnchor.UpperRight);
            UiKit.Place(_axisTop.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -52), new Vector2(38, 14));

            _axisBottom = UiKit.Label(root, "", 10, Palette.SubText, TextAnchor.LowerRight);
            UiKit.Place(_axisBottom.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 80), new Vector2(38, 14));
            _planLabel = UiKit.Label(root, "", 10, Palette.Accent, TextAnchor.MiddleLeft);
            _planLabel.name = "PlanBadge_" + companyId;
            _planLabel.rectTransform.anchorMin = Vector2.zero; _planLabel.rectTransform.anchorMax = new Vector2(1, 0);
            _planLabel.rectTransform.offsetMin = new Vector2(8, 40); _planLabel.rectTransform.offsetMax = new Vector2(-8, 68);

            foreach (int day in new[] { 0, 10, 20, 30 })
            {
                var tick = UiKit.Label(chartRt, day + "일", 9, Palette.SubText, TextAnchor.UpperCenter);
                UiKit.Place(tick.rectTransform, new Vector2((day + 0.5f) / DayAxis.Count, 0), new Vector2(0.5f, 1), new Vector2(0, -1), new Vector2(40, 14));
            }

            // 묶음 버튼 6개
            var row = UiKit.Rect(root, "Bundles");
            row.anchorMin = new Vector2(0, 0);
            row.anchorMax = new Vector2(1, 0);
            row.pivot = new Vector2(0.5f, 0);
            row.offsetMin = new Vector2(6, 6);
            row.offsetMax = new Vector2(-6, 38);
            for (int k = 0; k < UiRules.BundleCount; k++)
            {
                int bundleIndex = k;
                var b = UiKit.Button(row, "", 10, Palette.Disabled, Palette.Text, () => OnBundleClicked(bundleIndex));
                UiKit.Anchor((RectTransform)b.transform, k / 6f, 0, (k + 1) / 6f, 1, 2, 0);
                _bundleButtons[k] = b;
            }
        }

        private void OnBundleClicked(int bundleIndex)
        {
            _c.BuyInformation(_companyId, bundleIndex);
        }

        public void Refresh()
        {
            // 남은 자리 수는 정보 구매 창에 표시하지 않는다(자리 선택 화면에서만 보임). 내가 고른 기업 표시만 남김.
            _seats.text = _c.Store.OwnSelections.Contains(_companyId) ? "<color=#2563EB><b>내 선택</b></color>" : "";
            _expand.interactable = _c.Store.View != null;
            var intervals = _c.Store.TradePlans.ChartIntervals(_c.Store.View, _c.Store.CurrentTurn, _companyId, _c.Store.OrderLocked);
            _planLabel.text = _c.Store.OrderLocked ? (intervals.Length == 0 ? "" : "확정: " + string.Join(", ", intervals.Select(i => i.entryDay + "→" + i.exitDay + "일"))) : _c.Store.TradePlans.Summary(_companyId);

            var company = _c.Store.View?.companies?.FirstOrDefault(x => x.companyId == _companyId);
            var revealed = _c.Store.Revealed != null && _c.Store.RevealedTurn == _c.Store.CurrentTurn
                ? _c.Store.Revealed.FirstOrDefault(x => x.companyId == _companyId)
                : null;
            DrawChart(company, revealed);
            RefreshButtons();
        }

        private void DrawChart(VisibleCompany company, RevealedCompany revealed)
        {
            int cap = GraphSeriesBuilder.AxisLimitBps(_companyId); // 공개 설정값. 비공개 원본을 보지 않는다.
            _axisTop.text = DisplayFormat.Percent(cap);
            _axisBottom.text = DisplayFormat.Percent(-cap);
            // 시장 공개(주문 잠금) 이후에만 31일 전체, 그 전에는 열람한 날짜만.
            _bars = DailyBarModel.Build(company, revealed);
            _chart.SetData(_bars, cap);
            _chart.SetTradeIntervals(_c.Store.TradePlans.ChartIntervals(_c.Store.View, _c.Store.CurrentTurn, _companyId, _c.Store.OrderLocked), !_c.Store.OrderLocked);
        }

        private void OnHover(int? day)
        {
            _tip.text = day.HasValue && day.Value < _bars.Length ? DailyBarModel.Tooltip(_bars[day.Value]) : "";
        }

        private void RefreshButtons()
        {
            for (int k = 0; k < _bundleButtons.Length; k++)
                CompanyInfo.RefreshPurchaseButton(_bundleButtons[k], _c, _companyId, k);
        }
    }
}
