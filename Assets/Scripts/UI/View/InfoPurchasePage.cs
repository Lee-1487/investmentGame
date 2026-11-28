// 정보 구매 창: 기업 카드 6개(위 3개·아래 3개, REQ-005).
// 카드마다 일별 그래프와 이동형 5일 창. 고정 묶음 버튼은 v2 fixture 비교용이다.
// 코인·열람 상태는 서버 응답으로만 바뀐다. 버튼은 요청 의도만 보낸다.
using System.Collections.Generic;
using System;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BlackWhale.UI.View
{
    public static class CompanyInfo
    {
        public static string RevealedSummary(RevealedCompany company)
            => string.Join("   ", (company?.investments ?? Array.Empty<RevealedInvestment>()).Select(p =>
                "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Player(p.playerId)) + ">■ " + p.playerId + " " + DisplayFormat.Percent(p.returnBps) + "</color>"));

        public static string Volatility(string companyId)
        {
            switch (companyId)
            {
                case "AI":
                case "FOOD":
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
        private readonly CoinBadge _coinBadge;
        private readonly Text _title, _publicHistory;
        private readonly List<CompanyCard> _cards = new List<CompanyCard>();
        private readonly RectTransform _grid, _zoom, _planRows;
        private readonly BarChartGraphic _zoomChart;
        private readonly ChartPointerLayer _zoomPointer;
        private readonly Text _zoomTitle, _zoomTip, _zoomMessage;
        private readonly Button _favorite, _cancelEntry;
        private readonly Button[] _rankButtons = new Button[7];
        private readonly Button[] _zoomBundleButtons = new Button[UiRules.BundleCount];
        private readonly Text _zoomPurchaseStatus;
        private readonly Text _zoomProbability;
        private readonly Button _rumorsToggle, _zoomRumorsToggle;
        private string _zoomCompany, _zoomPhase, _rowKey, _note = "";
        private int _zoomTurn;
        private int? _entryDay, _hoverDay;
        private DayBar[] _zoomBars = new DayBar[0];
        private readonly InformationWindowSelector _zoomWindow;
        private readonly RectTransform _zoomLegacyPurchases;
        private readonly Button _interactionMode, _blackbox;
        private readonly RectTransform _informationItems;
        private string _itemsKey;
        private bool _selectInformation;
        private bool _showInformationItems;
        private Text _rankLabel;

        public InfoPurchasePage(RectTransform page, GameClientController controller, RectTransform overlayParent = null)
        {
            _c = controller;

            var title = _title = UiKit.Label(page, "정보 구매 창", 18, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(6, -2), new Vector2(300, 28));

            // 보유 코인: 금화 그림 + 개수(오른쪽 끝). 자산 등 나머지는 그 왼쪽 글자로.
            _coinBadge = new CoinBadge(page, 18);
            _coinBadge.Root.name = "HeaderCoinBadge";
            UiKit.Place(_coinBadge.Root, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-6, 0), new Vector2(84, 32));
            _header = UiKit.Label(page, "", 18, Palette.Text, TextAnchor.MiddleRight); // 자산: 크게
            UiKit.Place(_header.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-96, 0), new Vector2(560, 32));
            _header.resizeTextForBestFit = true; _header.resizeTextMinSize = 13; _header.resizeTextMaxSize = 18; // 길면 칸 안에 맞게
            _rumorsToggle = UiKit.Button(page, "", 11, Palette.AccentSoft, Palette.Text, _c.Store.ToggleRumors);
            _rumorsToggle.name = "ToggleInformationRumors";
            UiKit.Place((RectTransform)_rumorsToggle.transform, Vector2.one, Vector2.one, new Vector2(-6, -2), new Vector2(142, 28));
            _publicHistory = UiKit.Label(page, "", 11, Palette.SubText, TextAnchor.MiddleLeft);
            _publicHistory.name = "PublicInformationHistory";
            _publicHistory.rectTransform.anchorMin = new Vector2(0, 1); _publicHistory.rectTransform.anchorMax = Vector2.one;
            _publicHistory.rectTransform.offsetMin = new Vector2(6, -72); _publicHistory.rectTransform.offsetMax = new Vector2(-6, -32);

            var grid = _grid = UiKit.Rect(page, "CompanyGrid");
            UiKit.Stretch(grid, 0, 34, 0, 0);

            var ids = UiRules.CompanyIds;
            for (int i = 0; i < ids.Length; i++)
            {
                int col = i % 2;
                int row = i / 2; // 0 = 위, 1 = 아래
                var cell = UiKit.Rect(grid, "Card_" + ids[i]);
                UiKit.Anchor(cell, col / 2f, row == 0 ? 0.5f : 0f, (col + 1) / 2f, row == 0 ? 1f : 0.5f, 5, 5);
                _cards.Add(new CompanyCard(cell, ids[i], controller, Enlarge));
            }

            var zoomBg = UiKit.Panel(overlayParent ?? page, "ZoomCompanyPanel", Palette.Screen, rounded: true, raycast: true);
            UiKit.AddFrame(zoomBg, Palette.CardBorder);
            _zoom = zoomBg.rectTransform;
            // Reserve the top HUD and right-side controls, including the timer above the chart.
            UiKit.Stretch(_zoom, 4, 76, 168, 4);
            _zoomTitle = UiKit.Label(_zoom, "", 22, Palette.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            _zoomTitle.rectTransform.anchorMin = new Vector2(0, 1); _zoomTitle.rectTransform.anchorMax = Vector2.one;
            _zoomTitle.rectTransform.offsetMin = new Vector2(20, -48); _zoomTitle.rectTransform.offsetMax = new Vector2(-340, -12);
            var shrink = UiKit.Button(_zoom, "축소하기", 14, Palette.Card, Palette.Text, () => { Collapse(); Shrunk?.Invoke(); });
            shrink.name = "ShrinkChart";
            UiKit.Place((RectTransform)shrink.transform, Vector2.one, Vector2.one, new Vector2(-16, -12), new Vector2(110, 34));
            _interactionMode = UiKit.Button(_zoom, "", 12, Palette.AccentSoft, Palette.Accent, () => {
                _showInformationItems = !_showInformationItems; RefreshZoom(); });
            _interactionMode.name = "ToggleChartMode";
            UiKit.Place((RectTransform)_interactionMode.transform, Vector2.one, Vector2.one, new Vector2(-136, -12), new Vector2(188, 34));
            _zoomRumorsToggle = UiKit.Button(_zoom, "", 12, Palette.AccentSoft, Palette.Text, _c.Store.ToggleRumors);
            _zoomRumorsToggle.name = "ToggleZoomRumors";
            UiKit.Place((RectTransform)_zoomRumorsToggle.transform, Vector2.one, Vector2.one, new Vector2(-136, -12), new Vector2(160, 34));
            _zoomTip = UiKit.Label(_zoom, "", 13, Palette.SubText, TextAnchor.MiddleLeft);
            _zoomTip.rectTransform.anchorMin = new Vector2(0, 1); _zoomTip.rectTransform.anchorMax = Vector2.one;
            _zoomTip.rectTransform.offsetMin = new Vector2(24, -80); _zoomTip.rectTransform.offsetMax = new Vector2(-20, -50);
            var chart = UiKit.Rect(_zoom, "ZoomChart"); UiKit.Stretch(chart, 24, 88, 28, 228);
            _zoomChart = chart.gameObject.AddComponent<BarChartGraphic>();
            _zoomProbability = UiKit.Label(_zoom, "", 12, Palette.Text, TextAnchor.MiddleLeft);
            _zoomProbability.name = "ZoomReactionProbability";
            _zoomProbability.rectTransform.anchorMin = Vector2.zero; _zoomProbability.rectTransform.anchorMax = new Vector2(1, 0);
            _zoomProbability.rectTransform.offsetMin = new Vector2(24, 214); _zoomProbability.rectTransform.offsetMax = new Vector2(-24, 248);
            var pointer = _zoomPointer = chart.gameObject.AddComponent<ChartPointerLayer>();
            pointer.Clicked = PlanDay;
            pointer.Hovered = day => { _hoverDay = day; RefreshZoomHover(); };
            foreach (int day in new[] { 0, 5, 10, 15, 20, 25, 30 })
            {
                var tick = UiKit.Label(chart, day + "일", 11, Palette.SubText, TextAnchor.UpperCenter);
                UiKit.Place(tick.rectTransform, new Vector2((day + .5f) / DayAxis.Count, 0), new Vector2(.5f, 1), new Vector2(0, -2), new Vector2(44, 20));
            }
            _favorite = UiKit.Button(_zoom, "찜하기", 13, Palette.Card, Palette.Text,
                () => { if (CanPlan) _c.Store.TradePlans.SetFavorite(_zoomCompany, !_c.Store.TradePlans.Get(_zoomCompany).Favorite); });
            _favorite.name = "FavoriteCompany";
            UiKit.Place((RectTransform)_favorite.transform, Vector2.zero, Vector2.zero, new Vector2(20, 172), new Vector2(120, 32));
            var rankLabel = _rankLabel = UiKit.Label(_zoom, "우선순위", 13, Palette.SubText, TextAnchor.MiddleLeft);
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
            var planViewport = UiKit.Rect(_zoom, "PlannedIntervalScroll");
            planViewport.anchorMin = Vector2.zero; planViewport.anchorMax = new Vector2(1, 0);
            planViewport.offsetMin = new Vector2(20, 92); planViewport.offsetMax = new Vector2(-20, 164);
            _planRows = UiKit.ScrollContent(planViewport, "PlannedIntervals");
            _zoomMessage = UiKit.Label(_zoom, "", 12, Palette.SubText, TextAnchor.MiddleLeft);
            _zoomMessage.rectTransform.anchorMin = Vector2.zero; _zoomMessage.rectTransform.anchorMax = new Vector2(1, 0);
            _zoomMessage.rectTransform.offsetMin = new Vector2(20, 54); _zoomMessage.rectTransform.offsetMax = new Vector2(-20, 86);
            _zoomPurchaseStatus = UiKit.Label(_zoom, "", 13, Palette.SubText, TextAnchor.MiddleLeft);
            _zoomPurchaseStatus.name = "ZoomPurchaseStatus";
            UiKit.Place(_zoomPurchaseStatus.rectTransform, Vector2.zero, Vector2.zero, new Vector2(20, 10), new Vector2(260, 36));
            var purchaseRow = _zoomLegacyPurchases = UiKit.Rect(_zoom, "ZoomInformationBundles");
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
            var windowRow = UiKit.Rect(_zoom, "ZoomInformationWindow");
            windowRow.anchorMin = Vector2.zero; windowRow.anchorMax = new Vector2(1, 0);
            windowRow.offsetMin = new Vector2(286, 8); windowRow.offsetMax = new Vector2(-20, 50);
            _zoomWindow = new InformationWindowSelector(windowRow, _c, () => _zoomCompany);
            _zoomWindow.Changed = start => RefreshMouseRange();
            _zoomWindow.ControlsChanged = RefreshZoom;
            _zoomWindow.CanReserve = () => CanPlan && _c.Store.TradePlans.RangeError(_zoomCompany,
                _zoomWindow.Selection.SelectedIndex, _zoomWindow.StartDay, _zoomWindow.EndDay) == "";
            _zoomWindow.CanCancel = () => CanPlan;
            _zoomWindow.CancelReservation = () =>
            {
                var card = _cards.FirstOrDefault(c => c.CompanyId == _zoomCompany);
                if (card == null) return;
                _entryDay = null; _note = card.CancelSelected(); _rowKey = null; RefreshZoom();
            };
            _zoomWindow.Reserve = () => {
                if (!CanPlan) return;
                _note = _cards.First(c => c.CompanyId == _zoomCompany).SaveReservation();
                EventSystem.current?.SetSelectedGameObject(_zoomPointer.gameObject); RefreshZoom();
            };
            _blackbox = UiKit.Button(_zoom, "블랙박스 · 최대 5일", 11, Palette.AccentSoft, Palette.Accent,
                () => _c.UseInformationItem("BLACKBOX", _zoomCompany, _zoomWindow.StartDay));
            _blackbox.name = "UseBlackboxWindow";
            UiKit.Place((RectTransform)_blackbox.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-20, 172), new Vector2(160, 32));
            _informationItems = UiKit.Rect(_zoom, "InformationItems");
            _informationItems.anchorMin = Vector2.zero; _informationItems.anchorMax = new Vector2(1, 0);
            _informationItems.offsetMin = new Vector2(20, 92); _informationItems.offsetMax = new Vector2(-20, 164);
            _zoom.gameObject.SetActive(false);
        }

        private bool CanPlan => _zoomCompany != null && _c.CanPlanTrade(_zoomCompany);

        /// <summary>예약 취소: 버튼을 만든 뒤 목록이 바뀌었어도 같은 구간(진입·청산 값)을 찾아 지운다.
        /// 선택 표시·진입 후보도 함께 정리하고 결과를 안내한다.</summary>
        private void CancelReservation(int index, int entry, int exit)
        {
            if (!CanPlan) { _note = "지금은 예약을 바꿀 수 없습니다."; RefreshZoom(); return; }
            var plans = _c.Store.TradePlans.Get(_zoomCompany).Intervals;
            int at = index >= 0 && index < plans.Length && plans[index].entryDay == entry && plans[index].exitDay == exit
                ? index : Array.FindIndex(plans, p => p.entryDay == entry && p.exitDay == exit);
            _zoomWindow.Selection.Clear();
            _entryDay = null;
            bool removed = at >= 0 && _c.Store.TradePlans.RemoveInterval(_zoomCompany, at);
            _note = removed ? entry + "→" + exit + "일 예약을 취소했습니다." : "이미 취소된 예약입니다.";
            _rowKey = null;
            _cards.FirstOrDefault(c => c.CompanyId == _zoomCompany)?.Refresh();
            RefreshZoom();
        }
        /// <summary>사용자가 '축소하기'를 눌렀을 때(확대를 연 화면으로 돌아가기용).</summary>
        public Action Shrunk;
        public void OpenCompany(string company) => Enlarge(company);
        private void Enlarge(string company)
        {
            _zoomCompany = company; _zoomPhase = _c.Store.Public?.phase; _zoomTurn = _c.Store.CurrentTurn;
            _entryDay = _hoverDay = null; _note = ""; _rowKey = null;
            _selectInformation = _c.Store.Public?.windowInformation == true && _c.Store.Public?.phase == Protocol.Info;
            _zoomWindow.Selection = _cards.First(c => c.CompanyId == company).Selection;
            _zoomPointer.RangeSelection = _c.Store.Public?.windowInformation == true ? _zoomWindow.Selection : null;
            _zoomPointer.CanEditRange = () => string.IsNullOrEmpty(_c.Store.EndReason) && _c.Store.Public?.paused != true &&
                (_c.Store.Public?.phase == Protocol.Info && !(_c.Store.Public.stockSystem && _c.Store.Public.publicSelectionActive) || CanPlan);
            _zoomPointer.Intervals = () => CanPlan ? _c.Store.TradePlans.Get(_zoomCompany).Intervals : Array.Empty<Interval>();
            _zoomPointer.EditInterval = (index, entry, exit) => CanPlan ? _c.Store.TradePlans.TryReplaceInterval(_zoomCompany, index, entry, exit) : ErrorCodes.OrderLocked;
            _zoomPointer.DeleteInterval = index => { if (CanPlan) _c.Store.TradePlans.RemoveInterval(_zoomCompany, index); };
            _zoomPointer.RangeChanged = RefreshMouseRange;
            _zoomPointer.RangeFinished = () =>
            {
                var exchangeCard = _cards.First(c => c.CompanyId == _zoomCompany);
                if (exchangeCard.HandleExchangeSelection()) { RefreshZoom(); return; }
                var card = _cards.FirstOrDefault(c => c.CompanyId == _zoomCompany);
                if (card == null) return;
                if (card.AutoReserve) _note = card.SaveReservation();
                RefreshZoom();
            };
            _grid.gameObject.SetActive(false); _zoom.gameObject.SetActive(true); _zoom.SetAsLastSibling();
            _header.gameObject.SetActive(false); _coinBadge.SetVisible(false); // 확대 창 오른쪽 틈으로 뒤쪽 머리글이 비쳐 보이지 않게
            _zoomChart.name = "ZoomChart_" + company; RefreshZoom();
        }
        public void Collapse()
        { _zoomCompany = null; _entryDay = _hoverDay = null; _grid.gameObject.SetActive(true); _zoom.gameObject.SetActive(false); _header.gameObject.SetActive(true); _coinBadge.SetVisible(_c.Store.View != null); }
        private void PlanDay(int day, bool right)
        {
            if (_selectInformation && _c.Store.Public?.windowInformation == true)
            { if (!right) _zoomWindow.SetStart(day); return; }
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
                if (error.Length > 0) _note = Palette.BadText(ErrorMessages.ToKorean(error));
                else { _entryDay = null; _note = "개인 계획을 저장했습니다. 선택한 기업의 투자 초안에 적용됩니다."; }
            }
            RefreshZoom();
        }
        private void RefreshZoom()
        {
            if (_zoomCompany == null) return;
            var store = _c.Store; var plan = store.TradePlans.Get(_zoomCompany);
            bool stockSystem = store.Public?.stockSystem == true;
            _zoomRumorsToggle.gameObject.SetActive(stockSystem);
            UiKit.SetButton(_zoomRumorsToggle, store.RumorsVisible ? "찌라시 숨기기" : "찌라시 보이기", true, Palette.AccentSoft, Palette.Text);
            _zoomProbability.gameObject.SetActive(stockSystem);
            _zoomProbability.rectTransform.offsetMin = new Vector2(24, stockSystem && store.Public?.phase == Protocol.Info && !_showInformationItems ? 100 : 214);
            _zoomProbability.rectTransform.offsetMax = new Vector2(-24, stockSystem && store.Public?.phase == Protocol.Info && !_showInformationItems ? 128 : 248);
            _zoomProbability.text = BoardChartAnnotations.ProbabilityLabel(store.Public, store.View?.portfolio, _zoomCompany);
            _zoomChart.rectTransform.offsetMin = new Vector2(24, stockSystem && store.Public?.phase == Protocol.Info && !_showInformationItems ? 150 : stockSystem ? 260 : 228);
            _favorite.gameObject.SetActive(!stockSystem);
            foreach (var rankButton in _rankButtons) rankButton.gameObject.SetActive(!stockSystem);
            _rankLabel.gameObject.SetActive(!stockSystem); // 주식 모드에선 순위 버튼이 없으니 '우선순위' 글자만 남지 않게
            bool windows = store.Public?.windowInformation == true;
            if (store.Public?.publicSelectionActive == true) _selectInformation = true;
            _interactionMode.gameObject.SetActive(windows && (store.View?.inventory?.Length ?? 0) > 0 && store.Public?.publicSelectionActive != true);
            UiKit.SetButton(_interactionMode, _showInformationItems ? "예약 목록 보기" : "정보 아이템 보기", true, Palette.AccentSoft, Palette.Accent);
            _zoomLegacyPurchases.gameObject.SetActive(!windows);
            var exchangeCard = _cards.First(c => c.CompanyId == _zoomCompany);
            exchangeCard.ConfigureExchange(_zoomWindow, RefreshZoom);
            _zoomWindow.Refresh();
            _zoomMessage.gameObject.SetActive(!_zoomWindow.ChoosingRecipient);
            _zoomChart.SetInformationRange(windows && !_zoomWindow.InExchangeMode && !_zoomWindow.HasIncomingExchange && _zoomWindow.Selection.HasSelection
                ? new Vector2Int(_zoomWindow.StartDay, _zoomWindow.EndDay) : (Vector2Int?)null);
            _zoomChart.SetExchangeOverlays(exchangeCard.ExchangeSendStart, exchangeCard.ExchangeReceiveStart);
            _planRows.gameObject.SetActive(!_showInformationItems && !(stockSystem && store.Public?.phase == Protocol.Info));
            _informationItems.gameObject.SetActive(windows && _showInformationItems);
            RefreshInformationItems();
            _blackbox.gameObject.SetActive(windows && (store.View?.inventory?.Contains("BLACKBOX") ?? false));
            _cancelEntry.gameObject.SetActive(!windows);
            _blackbox.interactable = store.Public?.phase == Protocol.Info && store.Public?.publicSelectionActive != true && store.Public?.paused != true &&
                !store.HasPending(CommandTypes.UseInformationItem) && _zoomWindow.Selection.HasSelection && InformationWindowRules.NewDays(store.View?.companies?.FirstOrDefault(c => c.companyId == _zoomCompany), _zoomWindow.StartDay, Math.Min(30, _zoomWindow.StartDay + 4)) > 0;
            var company = store.View?.companies?.FirstOrDefault(c => c.companyId == _zoomCompany);
            var revealed = store.RevealedTurn == store.CurrentTurn ? store.Revealed?.FirstOrDefault(c => c.companyId == _zoomCompany) : null;
            _zoomBars = DailyBarModel.Build(company, revealed);
            _zoomChart.SetCompany(_zoomCompany);
            _zoomChart.SetSegmentKnowledge(store.Public?.segmentKnowledge, _zoomCompany);
            _zoomChart.SetData(_zoomBars);
            _zoomChart.SetTrendThroughDay(revealed != null ? 30 : store.Public?.stockSystem == true && store.Public.phase == Protocol.Investment ? store.View?.portfolio?.processedDay : (int?)null);
            _zoomChart.SetHiddenTail(!stockSystem && store.Public?.phase == Protocol.Info);
            _zoomChart.SetCurrentDay(StockRules.CurrentDay(store.Public, store.View?.portfolio));
            var intervals = store.TradePlans.ChartIntervals(store.View, store.CurrentTurn, _zoomCompany, store.OrderLocked);
            _zoomChart.ShowTradeLabels = false; // 예약·진입청산 구간은 띠만(시가·종가 글자 없음)
            if (revealed != null) _zoomChart.SetRevealedInvestments(revealed.investments); // 시장 공개 결과: 공개된 모든 투자 구간을 표시
            else _zoomChart.SetTradeIntervals(stockSystem ? Array.Empty<Interval>() : intervals, !store.OrderLocked);
            _zoomChart.SetPlanOverlays(stockSystem ? BoardChartAnnotations.Build(store.TradePlans, store.View, _zoomCompany, _c.LocalPlayerId, store.RumorsVisible) : null);
            // 확대된 실시간 투자 차트에서만 본인의 체결 B/S를 표시합니다.
            // 투자 결과 확대 차트에서도 B/S는 본인 체결만 표시하고,
            // 다른 플레이어는 SetRevealedInvestments가 그리는 색상 구간으로만 표시합니다.
            var marketCompany = store.Public?.shareMarket?.FirstOrDefault(c => c.companyId == _zoomCompany);
            bool liveTrading = stockSystem && store.Public?.phase == Protocol.Investment && revealed == null;
            var resultFills = revealed?.investments?.Where(p => p.playerId == _c.LocalPlayerId)
                .SelectMany(p => (p.intervals ?? Array.Empty<RevealedTrade>())
                .SelectMany(v => new[] {
                    new StockFill { playerId = p.playerId, day = v.entryDay, quantity = 1 },
                    new StockFill { playerId = p.playerId, day = v.exitDay, quantity = -1 }
                })).ToArray();
            _zoomChart.SetFills(revealed != null
                ? resultFills
                : liveTrading
                    ? marketCompany?.fills?.Where(f => f.playerId == _c.LocalPlayerId).ToArray()
                    : null);
            _zoomChart.SetCandidate(_entryDay);
            Vector2Int? allowed = null;
            if (CanPlan && !_selectInformation && (_entryDay.HasValue || plan.Intervals.Length < UiRules.MaxIntervalsPerCompany))
                allowed = _entryDay.HasValue ? new Vector2Int(_entryDay.Value + 5, Math.Min(30, _entryDay.Value + 10)) : new Vector2Int(0, 25);
            _zoomChart.SetAllowed(allowed);
            _zoomTitle.text = CompanyCatalog.Get(_zoomCompany).DisplayName + " · " + _zoomCompany + "  <size=13>"
                + (revealed != null ? "전체 플레이어 투자 구간" : stockSystem ? PhaseNames.ForState(store.Public) : store.OrderLocked ? "확정 주문 진입·청산" : windows ? store.Public?.publicSelectionActive == true ? "공용 정보 선택" : "정보 구매 · 구간 예약" : "이번 턴 개인 투자 계획") + "</size>";
            _zoomPurchaseStatus.text = store.Public?.publicSelectionActive == true
                ? "공용 공개 " + (store.Public.publicSelectionIndex + 1) + "/4 · " + store.Public.publicSelectionPlayerId + " 차례"
                : store.Public?.phase == Protocol.Info
                ? "정보 구매 · 보유 " + (store.View?.coins ?? 0) + "코인" + (store.View?.ability?.freeInformationCredits > 0 ? " · 무료 " + store.View.ability.freeInformationCredits + "일" : "")
                : revealed != null ? "공개 결과 · 구간별 수익률은 위 목록에서 확인" : stockSystem && store.Public?.phase == Protocol.Investment ? "" : "정보 구매는 정보 탐색 단계에서만";
            if (stockSystem) _zoomPurchaseStatus.text = _zoomCompany == "FOOD" ? "기본 흐름 카드 · 실제 변동폭에는 투자말 효과가 더해집니다."
                : _zoomCompany == "AUTO" ? "기본 흐름 카드 · 투자말 수에 영향을 받지 않습니다." : "투자말에 따라 구간 공개 때 주가가 정해집니다.";
            for (int i = 0; i < _zoomBundleButtons.Length; i++) CompanyInfo.RefreshPurchaseButton(_zoomBundleButtons[i], _c, _zoomCompany, i, true);
            UiKit.SetButton(_favorite, plan.Favorite ? "★ 찜 해제" : "☆ 찜하기", CanPlan, plan.Favorite ? Palette.AccentSoft : Palette.Card, Palette.Accent);
            for (int i = 0; i < _rankButtons.Length; i++)
                UiKit.SetButton(_rankButtons[i], i == 0 ? "미정" : i + "순위", CanPlan, plan.Rank == i ? Palette.Accent : Palette.Card, plan.Rank == i ? Color.white : Palette.Text);
            _cancelEntry.interactable = CanPlan && _entryDay.HasValue;
            string rowsKey = _zoomCompany + ":" + store.CurrentTurn + ":" + store.Public?.selectionIndex + ":" + (revealed != null) + ":" + store.OrderLocked + ":" + CanPlan + ":" + string.Join(";", intervals.Select(i => i.entryDay + "," + i.exitDay));
            if (_rowKey != rowsKey)
            {
                _rowKey = rowsKey; UiKit.Clear(_planRows);
                if (revealed?.investments != null) BuildRevealedRows(revealed);
                else
                {
                _planRows.sizeDelta = new Vector2(0, Math.Max(72, intervals.Length * 34));
                if (intervals.Length == 0)
                { var empty = UiKit.Label(_planRows, store.OrderLocked ? "이 기업에는 확정된 주문이 없습니다." : "진입일 시가 → 청산일 종가로 예약하세요. 보유 기간·구간 수 제한 없음", 14, Palette.SubText); UiKit.Stretch(empty.rectTransform); }
                for (int i = 0; i < intervals.Length; i++)
                {
                    int index = i; var interval = intervals[i];
                    var row = ScreenKit.TopStrip(_planRows, "Plan_" + i, i * 34, 30);
                    var label = UiKit.Label(row, (store.OrderLocked ? "확정 " : "예약 ") + (i + 1) + " · " + interval.entryDay + "일 시가 → " + interval.exitDay + "일 종가 (" + (interval.exitDay - interval.entryDay + (store.View?.entryAtOpen == true && interval.entryDay > 0 ? 1 : 0)) + "일 보유)", 14, Palette.Text, TextAnchor.MiddleLeft);
                    UiKit.Stretch(label.rectTransform, 0, 0, 90, 0);
                    if (CanPlan)
                    {
                        int entry = interval.entryDay, exit = interval.exitDay;
                        var remove = UiKit.Button(row, "예약 취소", 12, Palette.Card, Palette.Up, () => CancelReservation(index, entry, exit));
                        remove.name = "RemovePlan_" + i;
                        UiKit.Place((RectTransform)remove.transform, new Vector2(1, .5f), new Vector2(1, .5f), Vector2.zero, new Vector2(76, 28));
                    }
                }
                }
            }
            _zoomMessage.text = !CanPlan ? (store.OrderLocked ? "확정 주문은 바꿀 수 없습니다. 시장 공개 후 플레이어별 구간과 수익률을 표시합니다."
                : "계획은 정보 탐색·기업 선택 단계에서 편집합니다. 투자 단계에서는 투자 화면에서 구간을 변경하고 주문을 확정하세요.")
                : _note.Length > 0 ? _note : "선택한 기업의 예약은 투자 마감 때 자동 적용됩니다. 투자 화면에서 비중과 구간을 수정할 수 있습니다.";
            if (windows) _zoomMessage.text = revealed != null ? "플레이어 색 띠 = 확정 구간 · 아래 목록을 스크롤해 구간별 수익률을 확인하세요. 종목 합산은 해당 기업의 투자금 기준입니다."
                : "진입일 시가 → 청산일 종가 · 예약은 투자 마감 때 자동 적용 · 양 끝 조절 / 몸통 이동 / Del 삭제";
            if (stockSystem && revealed == null) _zoomMessage.text = store.Public?.phase == Protocol.Investment
                ? store.Public.stockSegmentRevealing ? "시장 공개 중입니다. 다음 구간 선택 시간에 투자말을 바꿀 수 있습니다." : "투자 화면에서 3개의 투자말을 배치하세요. 다른 플레이어의 이번 선택은 공개 시간에 표시됩니다."
                : _note;
            if (stockSystem && revealed == null && store.Public?.phase == Protocol.Investment && !store.OrderLocked)
            {
                int held = store.View?.portfolio?.holdings?.Where(h => h.companyId == _zoomCompany).Sum(h => h.shares) ?? 0;
                _zoomTitle.text += held > 0
                    ? "   <size=14><color=#A08A66>● 투자말 " + held + "개</color></size>"
                    : "   <size=14><color=#F2E6CC>● 투자말 0개</color></size>";
                _zoomPurchaseStatus.text = "";
            }
            if (stockSystem && revealed != null) _zoomMessage.text = "투자말 1개는 라운드 시작 자산의 1/3입니다. 구간별 수익을 합산한 최종 자산은 전광판에서 확인하세요.";
            RefreshZoomHover();
        }
        private void BuildRevealedRows(RevealedCompany revealed)
        {
            float y = 0;
            foreach (var p in revealed.investments)
            {
                var header = UiKit.Label(_planRows, "■ " + p.playerId + " · 종목 합산 " + DisplayFormat.Percent(p.returnBps), 13,
                    Palette.Player(p.playerId), TextAnchor.MiddleLeft, FontStyle.Bold);
                header.name = "PlayerReturn_" + p.playerId;
                // Place directly in the scrolling content so all intervals remain reachable.
                header.rectTransform.anchorMin = new Vector2(0, 1); header.rectTransform.anchorMax = Vector2.one;
                header.rectTransform.offsetMin = new Vector2(0, -y - 24); header.rectTransform.offsetMax = new Vector2(0, -y);
                y += 24;
                foreach (var v in p.intervals)
                {
                    var text = UiKit.Label(_planRows, "    " + v.entryDay + "일 시가 → " + v.exitDay + "일 종가  ·  구간 " + DisplayFormat.Percent(v.returnBps),
                        12, Palette.Player(p.playerId), TextAnchor.MiddleLeft);
                    text.rectTransform.anchorMin = new Vector2(0, 1); text.rectTransform.anchorMax = Vector2.one;
                    text.rectTransform.offsetMin = new Vector2(0, -y - 24); text.rectTransform.offsetMax = new Vector2(0, -y);
                    y += 24;
                }
            }
            if (y == 0) { var label = UiKit.Label(_planRows, "이 기업에는 투자한 플레이어가 없습니다.", 13, Palette.SubText); UiKit.Stretch(label.rectTransform); }
            _planRows.sizeDelta = new Vector2(0, Math.Max(72, y));
        }
        private void RefreshMouseRange()
        {
            if (_zoomCompany == null) return;
            var selection = _zoomWindow.Selection;
            _zoomChart.SetInformationRange(selection.HasSelection ? new Vector2Int(selection.Start, selection.End) : (Vector2Int?)null);
            _zoomWindow.Refresh();
            _zoomMessage.gameObject.SetActive(!_zoomWindow.ChoosingRecipient);
            if (!string.IsNullOrEmpty(_zoomPointer.LastEditError)) _zoomTip.text = Palette.BadText(ErrorMessages.ToKorean(_zoomPointer.LastEditError));
            else if (selection.HasSelection)
            {
                int? rate = DailyBarModel.IntervalReturnBps(_zoomBars, selection.Start, selection.End, _c.Store.View?.entryAtOpen == true);
                _zoomTip.text = selection.Start + "일 시가 → " + selection.End + "일 종가 · " + (rate.HasValue ? DisplayFormat.Percent(rate.Value) : "미열람 끝점") + " · 보유 기간·구간 수 제한 없음";
            }
        }
        private void RefreshInformationItems()
        {
            var state = _c.Store.Public; var items = _c.Store.View?.inventory ?? Array.Empty<string>();
            string steal = items.Contains("INFORMATION_STEAL") ? "INFORMATION_STEAL" : items.Contains("STEAL") ? "STEAL" : null;
            bool distort = items.Contains("DISTORT");
            bool enabled = state?.phase == Protocol.Info && state?.publicSelectionActive != true && state?.paused != true &&
                string.IsNullOrEmpty(_c.Store.EndReason) && !_c.Store.HasPending(CommandTypes.UseInformationItem);
            string key = steal + "|" + distort + "|" + enabled + "|" + state?.publicSelectionIndex + "|" + state?.publicSelectionActive + "|" + string.Join(",", state?.players?.Select(p => p.playerId) ?? Enumerable.Empty<string>());
            if (_itemsKey == key) return;
            _itemsKey = key; UiKit.Clear(_informationItems);
            var note = UiKit.Label(_informationItems, steal == null && !distort ? "공용 정보는 전원 공개 · 구매·무료·아이템 정보는 본인에게만 보입니다." : "정보 공격 · 상대의 유료 구매 기록에만 적용됩니다.", 12, Palette.SubText, TextAnchor.UpperLeft);
            if (state?.publicSelectionActive == true) note.text = "공용 공개: " + string.Join(" / ", (state.publicInformation ?? Array.Empty<PublicInformationChoice>()).Select(InformationWindowRules.PublicChoiceLabel));
            UiKit.Stretch(note.rectTransform);
            int slot = 0;
            foreach (var player in state?.players ?? Array.Empty<Player>())
            {
                if (player.playerId == _c.LocalPlayerId) continue;
                string target = player.playerId;
                foreach (string item in new[] { steal, distort ? "DISTORT" : null })
                {
                    if (item == null) continue;
                    string id = item;
                    var button = UiKit.Button(_informationItems, target + (id == "DISTORT" ? " 왜곡" : " 훔치기"), 12,
                        Palette.AccentSoft, Palette.Accent, () => _c.UseInformationItem(id, targetPlayerId: target));
                    button.name = "UseInformationItem_" + id + "_" + target; button.interactable = enabled;
                    UiKit.Place((RectTransform)button.transform, Vector2.zero, Vector2.zero, new Vector2(slot++ * 132, 0), new Vector2(124, 32));
                }
            }
        }
        private void RefreshZoomHover()
        {
            _zoomChart.SetPreview(null, false);
            if (!_hoverDay.HasValue || _hoverDay.Value >= _zoomBars.Length)
            {
                _zoomTip.text = _c.Store.RevealedTurn == _c.Store.CurrentTurn && _c.Store.Revealed != null
                    ? "구간 수익률 = 청산일 종가 − 진입일 시가 · 마우스를 올리면 날짜별 값을 확인합니다."
                    : "미열람 구간은 주가를 표시하지 않습니다.";
                return;
            }
            int day = _hoverDay.Value; _zoomTip.text = DailyBarModel.Tooltip(_zoomBars[day]);
            string notes = BoardChartAnnotations.DayNotes(_zoomChart.PlanOverlays, day);
            if (notes.Length > 0) _zoomTip.text += " · " + notes;
            if (CanPlan && _entryDay.HasValue && day > _entryDay.Value)
            {
                var intervals = _c.Store.TradePlans.Get(_zoomCompany).Intervals.Concat(new[] { new Interval { entryDay = _entryDay.Value, exitDay = day } }).ToArray();
                _zoomChart.SetPreview(new Vector2Int(_entryDay.Value, day), OrderInputValidator.ValidateIntervals(intervals).Length == 0);
                int? ret = DailyBarModel.IntervalReturnBps(_zoomBars, _entryDay.Value, day, _c.Store.View?.entryAtOpen == true);
                _zoomTip.text += " · 구간 " + (ret.HasValue ? DisplayFormat.Percent(ret.Value) : "수익률 미확인");
            }
        }

        private string _draftPhaseKey = "";

        /// <summary>확정(예약·구매)하지 않은 선택 표시를 지운다: 각 카드의 선택 구간, 확대 차트의 진입 후보.
        /// 이미 예약·구매한 구간은 그대로 둔다.</summary>
        public void ClearUnconfirmed()
        {
            bool changed = _entryDay.HasValue;
            _entryDay = null;
            foreach (var card in _cards) changed |= card.ClearDraft();
            if (!changed) return;
            if (_zoomCompany != null) { _note = ""; RefreshZoom(); }
        }

        public void Refresh()
        {
            var v = _c.Store.View;
            var phase = _c.Store.Public?.phase;
            var state = _c.Store.Public;
            _rumorsToggle.gameObject.SetActive(state?.stockSystem == true);
            UiKit.SetButton(_rumorsToggle, _c.Store.RumorsVisible ? "찌라시 숨기기" : "찌라시 보이기", true, Palette.AccentSoft, Palette.Text);
            _header.rectTransform.anchoredPosition = new Vector2(state?.stockSystem == true ? -160 : -96, 0);
            string draftKey = (state?.turn ?? 0) + ":" + (state?.phase ?? "") + ":" + (state?.developerRevision ?? 0);
            if (draftKey != _draftPhaseKey) { _draftPhaseKey = draftKey; ClearUnconfirmed(); } // 다음 단계로 넘어가면 확정 안 한 선택은 지움
            bool windows = state?.windowInformation == true;
            bool showingResults = phase == Protocol.Reveal || phase == Protocol.Result;
            _title.text = showingResults ? "시장 공개 · 투자 결과" : state?.stockSystem == true ? phase == Protocol.Info ? "정보 탐색 · 구간 예약" : PhaseNames.ForState(state) : state?.publicSelectionActive == true ? "공용 정보 선택" : "정보 구매 창";
            _publicHistory.gameObject.SetActive(windows);
            UiKit.Stretch(_grid, 0, windows ? 76 : 34, 0, 0);
            if (windows) _publicHistory.text = showingResults ? "" /* 결과 안내 문구는 표시하지 않음 */
                : (state.publicSelectionActive ? "기업 선택 · " + (state.lobbySettings?.publicRevealDays ?? 5) + "일 무료 공개 · "
                    + (state.lobbySettings?.scatteredPublicDays == true ? "무작위 날짜 · " : "연속 구간 · ") : "새 정보 1일 = 1코인 · 마우스로 선택 후 정보 사기 / 예약하기 · ")
                + string.Join(" / ", (state.publicInformation ?? Array.Empty<PublicInformationChoice>()).Select(InformationWindowRules.PublicChoiceLabel));
            _coinBadge.SetVisible(v != null && _zoomCompany == null);
            if (v != null) _coinBadge.Set(v.coins);
            _header.text = v == null
                ? "개인 정보 수신 대기"
                : "자산 " + DisplayFormat.Money(v.assets)
                  + (phase == Protocol.Info ? state?.publicSelectionActive == true ? " · " + state.publicSelectionPlayerId + " 공개 차례" : "   ·   1코인 = 1일"
                     : state?.stockSystem == true ? "" : "   ·   구매는 정보 탐색 단계에서만");
            if (state?.stockSystem == true && !showingResults && !state.publicSelectionActive)
                _publicHistory.text = phase == Protocol.Info
                    ? "FOOD·AUTO 1~5일 공통 공개 · 나머지 카드 자동 배정 · 그래프에서 정보 교환 / 예약 구간에서 찌라시 보내기\n"
                      + string.Join(" / ", (v?.receivedTradeIntents ?? Array.Empty<TradeIntent>()).Select(i => i.senderId + "의 계획: " + i.companyId + " " + i.entryDay + "일 진입 → " + i.exitDay + "일 정산"))
                    : string.Join(" / ", (v?.portfolio?.insights ?? Array.Empty<StockInsight>()).Select(i => i.companyId + " " + i.message));
            if (state?.stockSystem == true) {
                _coinBadge.SetVisible(false);
                _header.text = v == null ? "개인 정보 수신 대기" : "자산 " + DisplayFormat.Money(v.assets) + " · 투자말 3개";
            }
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
        private readonly string _volatility;
        private readonly Image _border, _tradeShade;
        private readonly RectTransform _tradeBadge;
        private readonly Text _tradeBadgeText, _tradeShadeText;
        private readonly BarChartGraphic _chart;
        private readonly Text _tip;
        private readonly Text _planLabel, _probability;
        private readonly Button _expand;
        private DayBar[] _bars = new DayBar[0];
        private readonly Button[] _bundleButtons = new Button[UiRules.BundleCount];
        private readonly RectTransform _legacyBundles;
        private readonly InformationWindowSelector _window;
        private readonly ChartPointerLayer _pointer;
        private string _selectionKey = "";
        private bool _exchangeMode;
        private int? _exchangeSendStart, _exchangeReceiveStart;
        private string _exchangeTarget;
        private InformationExchange IncomingExchange => (_c.Store.View?.informationExchanges ?? Array.Empty<InformationExchange>())
            .FirstOrDefault(e => e.status == "PENDING" && e.targetPlayerId == _c.LocalPlayerId && e.offeredCompanyId == _companyId);
        private InformationExchange OutgoingExchange => (_c.Store.View?.informationExchanges ?? Array.Empty<InformationExchange>())
            .FirstOrDefault(e => e.status == "PENDING" && e.senderId == _c.LocalPlayerId && e.offeredCompanyId == _companyId);
        public int? ExchangeSendStart => IncomingExchange?.requestedStartDay ?? (_exchangeMode ? _exchangeSendStart : OutgoingExchange?.offeredStartDay);
        public int? ExchangeReceiveStart => IncomingExchange?.offeredStartDay ?? (_exchangeMode ? _exchangeReceiveStart : OutgoingExchange?.requestedStartDay);
        private bool IsExchangePhase => _c.Store.Public?.stockSystem == true && _c.Store.Public.phase == Protocol.Info;
        private bool CanSend(int start) => (_c.Store.View?.companies?.FirstOrDefault(c => c.companyId == _companyId)?.visibleWindows
            ?? Array.Empty<VisibleInformationWindow>()).Any(w => w.startDay == start && w.source == "DEALT" && w.dailyChangesBps?.Length == 5);
        private string ReceiveTarget(int start)
        {
            if (start < 6 || start > 26 || (_companyId != "FOOD" && _companyId != "AUTO")) return null;
            var company = _c.Store.View?.companies?.FirstOrDefault(c => c.companyId == _companyId);
            var known = InformationWindowRules.KnownDays(company);
            if (Enumerable.Range(start, 5).All(day => known[day].HasValue)) return null;
            var owners = (_c.Store.Public?.segmentKnowledge ?? Array.Empty<SegmentKnowledge>())
                .FirstOrDefault(k => k.companyId == _companyId && k.startDay == start)?.playerIds
                ?.Where(id => id != "ALL" && id != _c.LocalPlayerId).ToArray() ?? Array.Empty<string>();
            return owners.FirstOrDefault();
        }
        private void ResetExchange()
        { _exchangeMode = false; _exchangeSendStart = _exchangeReceiveStart = null; _exchangeTarget = null; }
        public bool HandleExchangeSelection()
        {
            if (!IsExchangePhase || !_exchangeMode) return false;
            if (Selection.HasSelection && CanSend(Selection.Start) && Selection.End == Selection.Start + 4)
                _exchangeSendStart = Selection.Start;
            Selection.Clear(); Refresh(); return true;
        }
        public void ConfigureExchange(InformationWindowSelector window, Action changed = null)
        {
            if (!IsExchangePhase) ResetExchange();
            var incoming = IsExchangePhase ? IncomingExchange : null;
            if (incoming != null) ResetExchange();
            window.HasIncomingExchange = incoming != null;
            window.InExchangeMode = incoming == null && _exchangeMode;
            window.ExchangeStatus = incoming != null
                ? incoming.senderId + " 요청 · 보낼 " + incoming.requestedStartDay + "~" + (incoming.requestedStartDay + 4) + "일 / 받을 " + incoming.offeredStartDay + "~" + (incoming.offeredStartDay + 4) + "일"
                : _exchangeMode ? "받을 " + _exchangeReceiveStart + "~" + (_exchangeReceiveStart + 4) + "일 · " + (_exchangeSendStart.HasValue ? "보낼 " + _exchangeSendStart + "~" + (_exchangeSendStart + 4) + "일" : "처음 배정받은 구간을 선택하세요")
                : OutgoingExchange != null ? "상대 수락 대기 중" : "";
            window.CanStartExchange = () => Selection.HasSelection && Selection.End == Selection.Start + 4 && OutgoingExchange == null && !_c.Store.HasPending(StockRules.ShareInformation) && ReceiveTarget(Selection.Start) != null;
            window.StartExchange = () => {
                if (window.CanStartExchange() != true) return;
                _exchangeReceiveStart = Selection.Start; _exchangeTarget = ReceiveTarget(Selection.Start);
                _exchangeMode = true; _exchangeSendStart = null; Selection.Clear(); Refresh(); changed?.Invoke();
            };
            window.CanRequestExchange = () => _exchangeMode && _exchangeSendStart.HasValue && CanSend(_exchangeSendStart.Value) && _exchangeReceiveStart.HasValue && !string.IsNullOrEmpty(_exchangeTarget) && OutgoingExchange == null;
            window.RequestExchange = () => {
                if (window.CanRequestExchange() != true) return;
                if (_c.ShareInformation(_exchangeTarget, _companyId, _exchangeSendStart.Value, _companyId, _exchangeReceiveStart.Value) == CommandStatus.Sent)
                { ResetExchange(); Selection.Clear(); Refresh(); changed?.Invoke(); }
            };
            window.CancelExchange = () => { ResetExchange(); Selection.Clear(); Refresh(); changed?.Invoke(); };
            window.AcceptExchange = () => { var request = IncomingExchange; if (request != null) _c.RespondInformationExchange(request.id, true); };
            window.RejectExchange = () => { var request = IncomingExchange; if (request != null) _c.RespondInformationExchange(request.id, false); };
        }
        public string CompanyId => _companyId;
        public int StartDay => _window.StartDay;
        public ChartRangeSelection Selection => _window.Selection;
        public void SetStartDay(int start) => _window.SetStart(start);

        public CompanyCard(RectTransform cell, string companyId, GameClientController controller, Action<string> enlarge = null)
        {
            _companyId = companyId;
            _c = controller;

            var bg = _border = UiKit.Panel(cell, "Bg", Palette.CardBorder, rounded: true); // 도트 테마: 황토 테두리
            UiKit.Stretch(bg.rectTransform);
            var inner = UiKit.Panel(bg.transform, "Inner", Palette.Card, rounded: true);
            UiKit.Stretch(inner.rectTransform, 2, 2, 2, 2);
            var root = inner.rectTransform;

            // NEXT-D03 기업 이미지(없으면 배지)와 표시명. 통신 ID는 그대로.
            var entry = CompanyCatalog.Get(companyId);
            var logo = CompanyBadge.Create(root, companyId, 24);
            UiKit.Place(logo, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -5), new Vector2(24, 24));
            // 이름은 한 줄만(두 줄로 넘어가면 아래 배지·그래프와 겹침). 변동성은 아래 안내 줄에 표시.
            var name = UiKit.Label(root, "<b>" + entry.DisplayName + "</b> <size=10>" + entry.Id + "</size>",
                14, Palette.Text, TextAnchor.MiddleLeft);
            name.rectTransform.anchorMin = new Vector2(0, 1); name.rectTransform.anchorMax = Vector2.one;
            name.rectTransform.offsetMin = new Vector2(38, -30); name.rectTransform.offsetMax = new Vector2(-84, -4);
            name.horizontalOverflow = HorizontalWrapMode.Wrap; name.verticalOverflow = VerticalWrapMode.Truncate;
            _volatility = string.IsNullOrEmpty(entry.Volatility) ? "" : "변동성 " + entry.Volatility.Replace("변동성", "").Trim();

            _expand = UiKit.Button(root, "확대하기", 11, Palette.AccentSoft, Palette.Accent, () => enlarge?.Invoke(_companyId));
            _expand.name = "Expand_" + companyId;
            UiKit.Place((RectTransform)_expand.transform, Vector2.one, Vector2.one, new Vector2(-6, -5), new Vector2(70, 24));

            _seats = UiKit.Label(root, "", 12, Palette.SubText, TextAnchor.UpperRight);
            UiKit.Place(_seats.rectTransform, Vector2.one, Vector2.one, new Vector2(-8, -31), new Vector2(90, 16));

            // 그래프 영역
            var chartRt = UiKit.Rect(root, "Chart");
            UiKit.Stretch(chartRt, 10, 54, 10, _c.Store.Public?.stockSystem == true ? 112 : 94);
            _chart = chartRt.gameObject.AddComponent<BarChartGraphic>();
            var pointer = _pointer = chartRt.gameObject.AddComponent<ChartPointerLayer>(); // 마우스 위치 날짜의 값 툴팁
            pointer.Hovered = OnHover;
            pointer.Clicked = (day, right) => { if (!right && _c.Store.Public?.windowInformation == true && !_c.Store.Public.publicSelectionActive) _window?.SetStart(day); };

            _tip = UiKit.Label(root, "", 10, Palette.Text, TextAnchor.MiddleLeft);
            _tip.rectTransform.anchorMin = new Vector2(0, 1); _tip.rectTransform.anchorMax = Vector2.one;
            _tip.rectTransform.offsetMin = new Vector2(10, -52); _tip.rectTransform.offsetMax = new Vector2(-104, -36); // 오른쪽 '내 선택' 표시와 안 겹치게
            _tip.horizontalOverflow = HorizontalWrapMode.Wrap; _tip.verticalOverflow = VerticalWrapMode.Truncate;
            _tip.resizeTextForBestFit = true; _tip.resizeTextMinSize = 8; _tip.resizeTextMaxSize = 10;
            _tip.text = _volatility;

            _planLabel = UiKit.Label(root, "", 10, Palette.Accent, TextAnchor.MiddleLeft);
            _planLabel.name = "PlanBadge_" + companyId;
            _planLabel.rectTransform.anchorMin = Vector2.zero; _planLabel.rectTransform.anchorMax = new Vector2(1, 0);
            _planLabel.rectTransform.offsetMin = new Vector2(8, 44); _planLabel.rectTransform.offsetMax = new Vector2(-8, 76);
            // 예약 구간이 많아도 이 칸 안에서만(넘치면 글자 크기를 줄임) — 그래프 위로 번지지 않게
            _planLabel.horizontalOverflow = HorizontalWrapMode.Wrap; _planLabel.verticalOverflow = VerticalWrapMode.Truncate;
            _planLabel.resizeTextForBestFit = true; _planLabel.resizeTextMinSize = 8; _planLabel.resizeTextMaxSize = 10;
            _probability = UiKit.Label(root, "", 10, Palette.Text, TextAnchor.UpperLeft);
            _probability.name = "InfoReactionProbability_" + companyId;
            _probability.rectTransform.anchorMin = Vector2.zero; _probability.rectTransform.anchorMax = new Vector2(1, 0);
            _probability.rectTransform.offsetMin = new Vector2(8, 80); _probability.rectTransform.offsetMax = new Vector2(-8, 108);

            foreach (int day in new[] { 0, 10, 20, 30 })
            {
                var tick = UiKit.Label(chartRt, day + "일", 9, Palette.SubText, TextAnchor.UpperCenter);
                UiKit.Place(tick.rectTransform, new Vector2((day + 0.5f) / DayAxis.Count, 0), new Vector2(0.5f, 1), new Vector2(0, -1), new Vector2(40, 14));
            }

            // 묶음 버튼 6개
            var row = _legacyBundles = UiKit.Rect(root, "Bundles");
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
            var windowRow = UiKit.Rect(root, "InformationWindow");
            windowRow.anchorMin = Vector2.zero; windowRow.anchorMax = new Vector2(1, 0);
            windowRow.offsetMin = new Vector2(6, 2); windowRow.offsetMax = new Vector2(-6, 42);
            _window = new InformationWindowSelector(windowRow, _c, () => _companyId);
            _window.Changed = start => DrawSelection();
            _window.ControlsChanged = Refresh;
            _window.CanReserve = () => CanPlan && _c.Store.TradePlans.RangeError(_companyId, Selection.SelectedIndex, Selection.Start, Selection.End) == "";
            _window.Reserve = () => { SaveReservation(); EventSystem.current?.SetSelectedGameObject(_pointer.gameObject); };
            _window.CanCancel = () => CanPlan;
            _window.CancelReservation = () => CancelSelected();
            pointer.CanEditRange = () => _c.Store.Public?.paused != true && string.IsNullOrEmpty(_c.Store.EndReason) &&
                (_c.Store.Public?.phase == Protocol.Info && !(_c.Store.Public.stockSystem && _c.Store.Public.publicSelectionActive) || CanPlan);
            pointer.Intervals = () => CanPlan ? _c.Store.TradePlans.Get(_companyId).Intervals : Array.Empty<Interval>();
            pointer.EditInterval = (index, entryDay, exitDay) => CanPlan ? _c.Store.TradePlans.TryReplaceInterval(_companyId, index, entryDay, exitDay) : ErrorCodes.OrderLocked;
            pointer.DeleteInterval = index => { if (CanPlan) _c.Store.TradePlans.RemoveInterval(_companyId, index); };
            pointer.RangeChanged = Refresh;
            pointer.RangeFinished = () => { if (!HandleExchangeSelection()) { if (AutoReserve) SaveReservation(); Refresh(); } };

            // 투자 결정(진입·청산) 단계: 매매할 수 있는 기업(주식 보유)은 파란 테두리 + '보유 N주' 배지,
            // 보유하지 않은 기업은 흐리게 덮고 '미보유'로 표시해 한눈에 구분되게 한다.
            _tradeShade = UiKit.Panel(root, "TradeLockShade", new Color(0f, 0f, 0f, 0.66f), rounded: true);
            UiKit.Stretch(_tradeShade.rectTransform);
            _tradeShadeText = UiKit.Label(_tradeShade.transform, "미보유 · 보유 0주 · 매수 가능", 13, Palette.SubText, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(_tradeShadeText.rectTransform, 8, 30, 8, 30);
            _tradeShade.gameObject.SetActive(false);
            var badge = UiKit.Panel(root, "TradeBadge", Palette.Accent, rounded: true);
            _tradeBadge = badge.rectTransform;
            UiKit.Place(_tradeBadge, Vector2.one, Vector2.one, new Vector2(-6, -32), new Vector2(76, 18));
            _tradeBadgeText = UiKit.Label(badge.transform, "", 11, Palette.OnAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(_tradeBadgeText.rectTransform);
            _tradeBadge.gameObject.SetActive(false);
            _expand.transform.SetAsLastSibling(); // 흐린 덮개 위에서도 '확대하기'는 보이고 눌리게
        }

        /// <summary>진입·청산 단계에서 이 기업을 매매할 수 있는지(보유 주식 수) 표시.</summary>
        private void RefreshTradeMark()
        {
            var s = _c.Store.Public;
            bool investing = s?.stockSystem == true && s.phase == Protocol.Investment && !_c.Store.OrderLocked;
            int shares = _c.Store.View?.portfolio?.holdings?.Where(h => h.companyId == _companyId).Sum(h => h.shares) ?? 0;
            bool tradable = investing && shares > 0;
            _border.color = tradable ? Palette.Accent : Palette.CardBorder;
            _border.transform.GetChild(0).GetComponent<RectTransform>().offsetMin = Vector2.one * (tradable ? 3f : 2f);
            _border.transform.GetChild(0).GetComponent<RectTransform>().offsetMax = -Vector2.one * (tradable ? 3f : 2f);
            _tradeBadge.gameObject.SetActive(tradable);
            _tradeBadgeText.text = "내 " + shares + "주";
            _tradeShade.gameObject.SetActive(false);
            if (tradable) _seats.text = "";
        }
        private bool CanPlan => _c.CanPlanTrade(_companyId);

        /// <summary>투자 결정 단계: 차트에서 구간을 고르면 '예약하기' 없이 바로 진입·청산으로 정한다(새 구간일 때).</summary>
        public bool AutoReserve => _c.Store.Public?.stockSystem == true && _c.Store.Public.phase == Protocol.Investment
            && CanPlan && Selection.HasSelection && Selection.SelectedIndex < 0 && Selection.End > Selection.Start;
        public string SaveReservation()
        {
            if (!CanPlan || !Selection.HasSelection) return "지금은 예약할 수 없습니다.";
            int index = Selection.SelectedIndex;
            if (index >= 0)
            {
                // 이미 같은 구간으로 정해져 있으면 다시 저장하지 않는다(바로 정하기 뒤 '예약하기'를 또 눌러도 중복 전송 없음)
                var saved = _c.Store.TradePlans.Get(_companyId).Intervals;
                if (index < saved.Length && saved[index].entryDay == Selection.Start && saved[index].exitDay == Selection.End)
                    return "이미 정해진 구간입니다.";
            }
            if (index < 0) index = _c.Store.TradePlans.Get(_companyId).Intervals.Length;
            string error = Selection.SelectedIndex >= 0 ? _c.Store.TradePlans.TryReplaceInterval(_companyId, index, Selection.Start, Selection.End)
                : _c.Store.TradePlans.TryAddInterval(_companyId, Selection.Start, Selection.End);
            if (error == "") { Selection.SelectInterval(index, Selection.Start, Selection.End); Refresh(); }
            return error == "" ? Palette.GoodText("예약했습니다.") + (_c.Store.Public?.stockSystem == true ? " 투자 화면에도 내 색으로 표시됩니다." : " 선택한 기업이면 투자 마감 때 자동 적용됩니다.") : Palette.BadText(ErrorMessages.ToKorean(error));
        }
        /// <summary>고른 예약(매수·청산 구간)을 취소한다. 결과 문구를 돌려준다.</summary>
        public string CancelSelected()
        {
            if (!CanPlan || Selection.SelectedIndex < 0) return "취소할 예약을 먼저 고르세요.";
            int index = Selection.SelectedIndex, entry = Selection.Start, exit = Selection.End;
            var plans = _c.Store.TradePlans.Get(_companyId).Intervals;
            if (index >= plans.Length || plans[index].entryDay != entry || plans[index].exitDay != exit)
                index = Array.FindIndex(plans, p => p.entryDay == entry && p.exitDay == exit);
            Selection.Clear();
            bool removed = index >= 0 && _c.Store.TradePlans.RemoveInterval(_companyId, index);
            Refresh();
            return removed ? entry + "→" + exit + "일 예약을 취소했습니다." : "이미 취소된 예약입니다.";
        }

        /// <summary>확정 안 한 선택 구간 표시를 지운다. 지운 것이 있으면 true.</summary>
        public bool ClearDraft()
        {
            if (!Selection.HasSelection) return false;
            Selection.Clear();
            Refresh();
            return true;
        }

        private void DrawSelection() => _chart.SetInformationRange(_c.Store.Public?.windowInformation == true && !_exchangeMode && IncomingExchange == null && Selection.HasSelection
            ? new Vector2Int(Selection.Start, Selection.End) : (Vector2Int?)null);

        private void OnBundleClicked(int bundleIndex)
        {
            _c.BuyInformation(_companyId, bundleIndex);
        }

        public void Refresh()
        {
            // 남은 자리 수는 정보 구매 창에 표시하지 않는다(자리 선택 화면에서만 보임). 내가 고른 기업 표시만 남김.
            string selectionKey = _c.Store.CurrentTurn + ":" + _c.Store.Public?.phase;
            if (_selectionKey != selectionKey) { _selectionKey = selectionKey; Selection.Clear(); ResetExchange(); } // 턴·단계가 바뀌면 확정 안 한 선택 지움
            var incomingEx = _c.Store.Public?.stockSystem == true && _c.Store.Public.phase == Protocol.Info
                ? (_c.Store.View?.informationExchanges ?? Array.Empty<InformationExchange>())
                    .FirstOrDefault(e => e.status == "PENDING" && e.targetPlayerId == _c.LocalPlayerId && e.offeredCompanyId == _companyId)
                : null;
            _seats.text = incomingEx != null ? "<color=#38BDF8><b>교환 요청</b></color>"
                : _c.Store.OwnSelections.Contains(_companyId) ? "<color=#A08A66><b>내 선택</b></color>" : "";
            _chart.SetExchangeOverlays(IsExchangePhase ? ExchangeSendStart : null, IsExchangePhase ? ExchangeReceiveStart : null);
            _expand.interactable = _c.Store.View != null;
            bool board = _c.Store.Public?.stockSystem == true;
            _probability.gameObject.SetActive(board);
            _probability.text = BoardChartAnnotations.ProbabilityLabel(_c.Store.Public, _c.Store.View?.portfolio, _companyId);
            var intervals = _c.Store.TradePlans.ChartIntervals(_c.Store.View, _c.Store.CurrentTurn, _companyId, _c.Store.OrderLocked);
            _planLabel.text = _c.Store.OrderLocked ? (intervals.Length == 0 ? "" : "확정: " + string.Join(", ", intervals.Select(i => i.entryDay + "→" + i.exitDay + "일"))) : _c.Store.TradePlans.Summary(_companyId);
            if (board) _planLabel.color = Palette.Player(_c.LocalPlayerId);

            var company = _c.Store.View?.companies?.FirstOrDefault(x => x.companyId == _companyId);
            var revealed = _c.Store.Revealed != null && _c.Store.RevealedTurn == _c.Store.CurrentTurn
                ? _c.Store.Revealed.FirstOrDefault(x => x.companyId == _companyId)
                : null;
            if (revealed != null) _planLabel.text = ""; // 시장 공개·투자 결과에서는 플레이어별 수익률 글자를 표시하지 않음
            // During exploration the empty reservation badge does not need its own row.
            bool exploring = board && _c.Store.Public.phase == Protocol.Info;
            _planLabel.gameObject.SetActive(!exploring);
            _chart.rectTransform.offsetMin = new Vector2(10, exploring ? 82 : board ? 112 : 94);
            _probability.rectTransform.offsetMin = new Vector2(8, exploring ? 48 : 80);
            _probability.rectTransform.offsetMax = new Vector2(-8, exploring ? 72 : 108);
            DrawChart(company, revealed);
            RefreshButtons();
            _probability.gameObject.SetActive(board && !_window.ChoosingRecipient);
            if (exploring && _window.ChoosingRecipient) _chart.rectTransform.offsetMin = new Vector2(10, 90);
            RefreshTradeMark();
        }

        private void DrawChart(VisibleCompany company, RevealedCompany revealed)
        {
            // 시장 공개(주문 잠금) 이후에만 31일 전체, 그 전에는 열람한 날짜만.
            _bars = DailyBarModel.Build(company, revealed);
            _chart.SetCompany(_companyId);
            _chart.SetSegmentKnowledge(_c.Store.Public?.segmentKnowledge, _companyId);
            _chart.SetData(_bars);
            _chart.SetTrendThroughDay(revealed != null ? 30 : _c.Store.Public?.stockSystem == true && _c.Store.Public.phase == Protocol.Investment ? _c.Store.View?.portfolio?.processedDay : (int?)null);
            _chart.SetHiddenTail(_c.Store.Public?.stockSystem != true && _c.Store.Public?.phase == Protocol.Info);
            _chart.SetCurrentDay(StockRules.CurrentDay(_c.Store.Public, _c.Store.View?.portfolio));
            _chart.SetFills(null); // 진입·청산 체결 막대는 그래프에 그리지 않음
            _chart.ShowTradeLabels = false; // 예약·진입청산 구간은 띠만(시가·종가 글자 없음)
            if (revealed != null) _chart.ClearTradeOverlays(); // 시장 공개·투자 결과: 진입·청산·수익률 표시 없이 주가만
            else _chart.SetTradeIntervals(_c.Store.Public?.stockSystem == true ? Array.Empty<Interval>() : _c.Store.TradePlans.ChartIntervals(_c.Store.View, _c.Store.CurrentTurn, _companyId, _c.Store.OrderLocked), !_c.Store.OrderLocked);
            _chart.SetPlanOverlays(_c.Store.Public?.stockSystem == true ? BoardChartAnnotations.Build(_c.Store.TradePlans, _c.Store.View, _companyId, _c.LocalPlayerId, _c.Store.RumorsVisible) : null);
        }

        private void OnHover(int? day)
        {
            _tip.text = day.HasValue && day.Value < _bars.Length ? DailyBarModel.Tooltip(_bars[day.Value]) : _volatility;
            if (day.HasValue) {
                string notes = BoardChartAnnotations.DayNotes(_chart.PlanOverlays, day.Value);
                if (notes.Length > 0) _tip.text += " · " + notes;
            }
        }

        private void RefreshButtons()
        {
            bool windows = _c.Store.Public?.windowInformation == true;
            _legacyBundles.gameObject.SetActive(!windows); ConfigureExchange(_window); _window.Refresh();
            _pointer.RangeSelection = windows ? Selection : null;
            DrawSelection();
            for (int k = 0; k < _bundleButtons.Length; k++)
                CompanyInfo.RefreshPurchaseButton(_bundleButtons[k], _c, _companyId, k);
        }
    }
}
