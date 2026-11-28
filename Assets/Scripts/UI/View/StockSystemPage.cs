using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace BlackWhale.UI.View
{
    public sealed class StockSystemPage
    {
        private sealed class Card
        {
            public Text Heading, Owners, History, Probability;
            public Text[] SegmentLabels;
            public Button[] Actions;
            public Image[] TokenSquares;
            public RectTransform SegmentPointerRoot, SegmentStartArrow, SegmentEndArrow, SegmentSpanLine;
            public BarChartGraphic Chart;
            public RectTransform Inventory, SliderRoot;
            public Slider Slider;
            public Image[] Units;
            public Text Count, Stamp;          // 손잡이 위 '내 n주', 매진 도장
            public Color[] LastColors;         // 직전 칸 색(새로 채워진 칸만 튀어 오르게)
            public bool WasSoldOut;
            public Outline Frame;              // (사용 안 함) 예전 매매 가능 테두리
            public Image TradableDot;          // 실시간 매매: 매매 가능하면 카드 왼쪽 위 흰 점
            public RectTransform MineBadge;    // '내 n주 · 매매 가능'
            public Text MineBadgeText;
            public Image Shade;                // 미보유 기업 흐린 덮개
            public Text TradeMarker;
            public int InitialShares;
            public int LiveTradeDirection;
        }
        // 연출(D): 현재 차례 시간 막대·긴박감·내 차례 알림
        private Image currentBar;
        private UrgencyEdges urgency;
        private RectTransform currentSlot;
        private Text currentSlotText;
        private int lastPulseSecond = -1;
        private bool myTurnNow;
        /// <summary>현재 차례 남은 시간(소수). TabletView의 서버 시계에서 받는다. 표시 연출에만 쓴다.</summary>
        public Func<double> RemainingExact;
        private readonly RectTransform root;
        private readonly GameClientController controller;
        private readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        private readonly Dictionary<string, int> targets = new Dictionary<string, int>();
        private string builtKey, draggingCompany, focus = "AI";
        private Text summary;
        private Button finish;
        private Button rumors;
        public Action<string> OpenPlan;
        public StockSystemPage(RectTransform parent, GameClientController controller) { root = parent; this.controller = controller; }
        private PublicState State => controller.Store.Public;
        private PortfolioState Portfolio => controller.Store.View?.portfolio;
        private string BuildKey => State.turn + ":" + State.phase + ":" + State.stockSegmentIndex + ":" + State.stockSegmentRevealing + ":" + State.selectionIndex + ":" + State.currentPlayerId + ":" + State.developerRevision;
        private bool Editable => controller.CanDraftShares();
        private int Holding(string id) => State.shareMarket?.FirstOrDefault(c => c.companyId == id)?.owners?.Where(v => v.playerId == controller.LocalPlayerId)
            .Sum(v => v.shares + v.reserved)
            ?? Portfolio?.holdings?.FirstOrDefault(v => v.companyId == id)?.shares ?? 0;
        public bool TryRefresh()
        {
            if (!controller.HasCurrentPlayerView || Portfolio == null || State.phase == StockRules.AugmentPhase || builtKey != BuildKey || cards.Count == 0) return false;
            RefreshCards(); return true;
        }
        public void Build()
        {
            cards.Clear(); targets.Clear(); draggingCompany = null; builtKey = BuildKey;
            if (!controller.HasCurrentPlayerView || Portfolio == null) { Label(root, "이번 턴의 주식·증강 정보를 기다리고 있습니다.", 0, 80); return; }
            if (State.phase == StockRules.AugmentPhase) { BuildAugments(); return; }
            foreach (string id in UiRules.CompanyIds) targets[id] = Holding(id);
            if (State.phase == Protocol.Selection) BuildOrderStrip();
            summary = Label(root, "", State.phase == Protocol.Investment ? 2 : 49, 29, 13); summary.name = "StockPurchaseSummary";
            summary.rectTransform.offsetMax = new Vector2(-145, summary.rectTransform.offsetMax.y);
            rumors = UiKit.Button(root, "", 11, Palette.AccentSoft, Palette.Text, controller.Store.ToggleRumors);
            rumors.name = "ToggleInvestmentRumors";
            UiKit.Place((RectTransform)rumors.transform, Vector2.one, Vector2.one,
                new Vector2(-4, State.phase == Protocol.Investment ? -2 : -49), new Vector2(136, 27));
            var grid = UiKit.Rect(root, "ShareMarketGrid"); UiKit.Stretch(grid, 0, State.phase == Protocol.Investment ? 34 : 82, 0, State.phase == Protocol.Investment ? 2 : 44);
            for (int i = 0; i < UiRules.CompanyIds.Length; i++)
            {
                string id = UiRules.CompanyIds[i];
                var cell = UiKit.Rect(grid, "ShareCard_" + id);
                UiKit.Anchor(cell, i % 2 / 2f, i < 2 ? .5f : 0, (i % 2 + 1) / 2f, i < 2 ? 1 : .5f, 4, 4);
                BuildCompany(cell, id);
            }
            if (State.phase == Protocol.Selection)
            {
                finish = UiKit.Button(root, "매수 완료 · 다음 차례", 14, Palette.Accent, Color.white, () => controller.PassStockDraft());
                finish.name = "PassShareDraft";
                UiKit.Place((RectTransform)finish.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-4, 3), new Vector2(190, 34));
                if (Portfolio.augments.Contains("PRIORITY") && !Portfolio.priorityUsed)
                {
                    var priority = UiKit.Button(root, "선점 사용", 12, Palette.AccentSoft, Palette.Accent, () => controller.UseDraftPriority());
                    priority.name = "UseDraftPriority";
                    UiKit.Place((RectTransform)priority.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(4, 3), new Vector2(110, 34));
                    int next = Array.FindIndex(State.draftOrder ?? Array.Empty<string>(), Math.Min(State.selectionIndex + 1, State.draftOrder?.Length ?? 0), id => id == controller.LocalPlayerId);
                    priority.interactable = !State.paused && next > State.selectionIndex + 1 && !controller.Store.HasPending(StockRules.Priority);
                }
            }
            else finish = null;
            RefreshCards();
            BuildEffects();
        }

        /// <summary>내 차례가 되면 배너가 올라왔다 사라지고, 마지막 3초에는 매수 화면 가장자리만 붉게 깜박인다.</summary>
        private void BuildEffects()
        {
            myTurnNow = State.phase == Protocol.Selection && State.currentPlayerId == controller.LocalPlayerId;
            lastPulseSecond = -1;
            urgency = null;
            if (State.phase != Protocol.Selection) return;

            // 주변 테두리 효과(마지막 3초 붉은 가장자리)는 쓰지 않음

            // 내 차례 알림도 가운데 큰 네모 배너 대신, 차례 줄 왼쪽 위의 주황 원으로만 표시한다.
        }

        /// <summary>매 프레임(선택 단계). 현재 차례 시간 막대를 줄이고, 3초 이하면 깜박임·초 단위 강조.</summary>
        public void Tick()
        {
            if (currentBar == null && urgency == null) return;
            if (State == null || State.phase != Protocol.Selection || State.developerMode || State.paused)
            {
                if (urgency != null) { urgency.SetLevel(0f); urgency.Update(); }
                return;
            }
            double remain = RemainingExact?.Invoke() ?? 0;
            float ratio = Mathf.Clamp01((float)(remain / StockRules.DraftSeconds));
            if (currentBar != null)
            {
                currentBar.rectTransform.anchorMax = new Vector2(ratio, 0);
                currentBar.color = ratio > .5f ? new Color32(0x10, 0xB9, 0x81, 0xFF) : ratio > .25f ? new Color32(0xD2, 0xB0, 0x82, 0xFF) : new Color32(0xFF, 0xFF, 0xFF, 0xFF); // 초록 → 회색 → 흰색
            }
            int sec = Mathf.CeilToInt((float)remain);
            bool hurry = remain > 0 && sec <= 3;
            if (hurry && sec != lastPulseSecond)
            {
                lastPulseSecond = sec;
                if (currentSlot != null) UiAnim.Pulse(currentSlot, 0f, 1.06f);
                if (myTurnNow && summary != null) UiAnim.Pulse(summary.rectTransform, 0f, 1.12f);
            }
            if (currentSlotText != null && currentSlotText.text.Contains("·"))
            {
                string head = currentSlotText.text.Split('<')[0];
                currentSlotText.text = head + "<size=12>" + (State.selectionIndex / 4 + 1) + "차 · " + (hurry ? "<color=#F2E6CC><b>" + sec + "초</b></color>" : sec + "초") + " · 지금</size>";
            }
            if (urgency != null)
            {
                urgency.SetLevel(hurry && myTurnNow ? 1f : 0f);
                urgency.Update();
            }
        }
        private void BuildOrderStrip()
        {
            var ids = State.draftOrder ?? Array.Empty<string>();
            currentBar = null; currentSlot = null; currentSlotText = null;
            int first = Math.Min(StockRules.DraftRounds - 1, State.selectionIndex / 4) * 4;
            for (int i = first; i < Math.Min(ids.Length, first + 4); i++)
            {
                bool current = i == State.selectionIndex && State.phase == Protocol.Selection;
                var panel = UiKit.Panel(root, "StockDraftSlot_" + i, current ? Palette.Maroon : Palette.Card, rounded: true);
                UiKit.AddFrame(panel, current ? Palette.MaroonBorder : Palette.CardBorder);
                panel.rectTransform.anchorMin = new Vector2((i - first) / 4f, 1); panel.rectTransform.anchorMax = new Vector2((i - first + 1) / 4f, 1);
                panel.rectTransform.offsetMin = new Vector2(3, -45); panel.rectTransform.offsetMax = new Vector2(-3, 0);
                var text = UiKit.Label(panel.transform, ids[i] + "  <size=12>" + (i / 4 + 1) + (State.developerMode ? "차 · 완료 시 진행" : "차 · 7초") + (current ? " · 지금" : "") + "</size>", 22, Palette.Player(ids[i]), TextAnchor.MiddleCenter, FontStyle.Bold);
                text.name = "StockDraftPlayer_" + i; UiKit.Stretch(text.rectTransform, 4, 1, 4, 1);
                if (current)
                {
                    // 현재 차례: 큰 네모 테두리 대신 왼쪽 위 주황 원 + 남은 시간 막대(초록→주황→빨강)
                    var dot = UiKit.Panel(panel.transform, "CurrentTurnDot", Palette.Accent);
                    dot.sprite = UiKit.Pill; dot.type = Image.Type.Simple; dot.preserveAspect = true;
                    UiKit.Place(dot.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -7), new Vector2(12, 12));
                    var track = UiKit.Panel(panel.transform, "DraftTimerTrack", Palette.Disabled, rounded: true);
                    track.rectTransform.anchorMin = Vector2.zero; track.rectTransform.anchorMax = new Vector2(1, 0);
                    track.rectTransform.offsetMin = new Vector2(8, 3); track.rectTransform.offsetMax = new Vector2(-8, 7);
                    currentBar = UiKit.Panel(track.transform, "DraftTimerBar", new Color32(0x10, 0xB9, 0x81, 0xFF), rounded: true);
                    currentBar.rectTransform.anchorMin = Vector2.zero; currentBar.rectTransform.anchorMax = Vector2.one;
                    currentBar.rectTransform.offsetMin = Vector2.zero; currentBar.rectTransform.offsetMax = Vector2.zero;
                    track.gameObject.SetActive(!State.developerMode);
                    currentSlot = dot.rectTransform; currentSlotText = text; // 마지막 몇 초 깜박임도 주황 원만
                    UiAnim.Pulse(dot.rectTransform, 0f, 1.6f);
                }
            }
        }
        private void BuildCompany(RectTransform cell, string id)
        {
            var panel = UiKit.Panel(cell, "ShareCardBackground", Palette.Card, rounded: true); UiKit.Stretch(panel.rectTransform);
            UiKit.AddFrame(panel, Palette.CardBorder);
            var r = panel.rectTransform;
            var card = new Card { InitialShares = targets.TryGetValue(id, out var initial) ? initial : Holding(id) }; cards[id] = card;
            // 1줄: 기업명·수량(왼쪽) | 내 보유 배지 · 확대(오른쪽)   2줄: 플레이어별 보유   3줄~: 차트
            // (예전에는 제목이 가운데 버튼 안에 있어 아래 줄 보유 표시와 겹쳐 보였음)
            card.Heading = Label(r, "", 5, 22, 14); card.Heading.name = "ShareHeading_" + id;
            card.Heading.rectTransform.offsetMin = new Vector2(10, card.Heading.rectTransform.offsetMin.y);
            card.Heading.rectTransform.offsetMax = new Vector2(-150, card.Heading.rectTransform.offsetMax.y);
            card.Heading.horizontalOverflow = HorizontalWrapMode.Overflow; // 리치 텍스트는 best-fit이 작게 잡혀 글자가 줄어들어서 고정 크기
            card.TradeMarker = UiKit.Label(r, "", 12, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            card.TradeMarker.name = "TradeDraftMarker_" + id;
            UiKit.Place(card.TradeMarker.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -35), new Vector2(70, 20));
            card.TradeMarker.gameObject.SetActive(false);
            var expand = UiKit.Button(r, "확대 ↗", 11, Palette.AccentSoft, Palette.Accent, () => OpenPlan?.Invoke(id));
            expand.name = "ExpandShare_" + id;
            UiKit.Place((RectTransform)expand.transform, Vector2.one, Vector2.one, new Vector2(-6, -5), new Vector2(58, 22));
            var badge = UiKit.Panel(r, "MineBadge_" + id, Palette.Accent, rounded: true);
            card.MineBadge = badge.rectTransform;
            UiKit.Place(card.MineBadge, Vector2.one, Vector2.one, new Vector2(-70, -5), new Vector2(74, 22));
            card.MineBadgeText = UiKit.Label(badge.transform, "", 11, Palette.OnAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(card.MineBadgeText.rectTransform);
            card.MineBadge.gameObject.SetActive(false);
            card.Owners = Label(r, "", 30, 18, 12); card.Owners.name = "ShareOwners_" + id;
            card.Owners.rectTransform.offsetMin = new Vector2(10, card.Owners.rectTransform.offsetMin.y);
            card.Owners.horizontalOverflow = HorizontalWrapMode.Wrap; card.Owners.verticalOverflow = VerticalWrapMode.Truncate;
            card.Owners.resizeTextForBestFit = true; card.Owners.resizeTextMinSize = 8; card.Owners.resizeTextMaxSize = 12;
            card.SegmentPointerRoot = UiKit.Rect(r, "NextSegmentPointer_" + id);
            card.SegmentPointerRoot.anchorMin = new Vector2(0, 1); card.SegmentPointerRoot.anchorMax = Vector2.one;
            card.SegmentPointerRoot.offsetMin = new Vector2(8, -51); card.SegmentPointerRoot.offsetMax = new Vector2(-8, -30);
            var spanBar = UiKit.Panel(card.SegmentPointerRoot, "SpanLine", new Color32(0xFF, 0x3B, 0x30, 0xD8));
            spanBar.raycastTarget = false;
            card.SegmentSpanLine = spanBar.rectTransform;
            var red = new Color32(0xFF, 0x3B, 0x30, 0xFF);
            var startArrow = UiKit.Label(card.SegmentPointerRoot, "▼", 13, red, TextAnchor.LowerCenter, FontStyle.Bold);
            startArrow.raycastTarget = false; startArrow.horizontalOverflow = HorizontalWrapMode.Overflow;
            card.SegmentStartArrow = startArrow.rectTransform;
            var endArrow = UiKit.Label(card.SegmentPointerRoot, "▼", 13, red, TextAnchor.LowerCenter, FontStyle.Bold);
            endArrow.raycastTarget = false; endArrow.horizontalOverflow = HorizontalWrapMode.Overflow;
            card.SegmentEndArrow = endArrow.rectTransform;
            card.SegmentPointerRoot.gameObject.SetActive(false);
            card.Frame = panel.gameObject.AddComponent<Outline>();
            card.Frame.effectColor = Palette.Accent; card.Frame.effectDistance = new Vector2(3, -3); card.Frame.enabled = false;
            card.TradableDot = UiKit.Panel(r, "TradableDot_" + id, Color.white);
            card.TradableDot.sprite = UiKit.Pill; card.TradableDot.type = Image.Type.Simple; card.TradableDot.preserveAspect = true;
            UiKit.Place(card.TradableDot.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(8, -11), new Vector2(9, 9));
            card.TradableDot.gameObject.SetActive(false);
            var chartRoot = UiKit.Rect(r, "TradeChart_" + id); UiKit.Stretch(chartRoot, 8, 53, 8, State.phase == Protocol.Investment ? 110 : State.phase != Protocol.Selection ? 8 : 54);
            card.Chart = chartRoot.gameObject.AddComponent<BarChartGraphic>(); card.Chart.SetInteractive(true);
            UiKit.Click(chartRoot.gameObject, () => OpenPlan?.Invoke(id));
            card.Shade = UiKit.Panel(r, "NotOwnedShade_" + id, new Color(0f, 0f, 0f, 0.66f), rounded: true);
            UiKit.Stretch(card.Shade.rectTransform, 0, 50, 0, 0);
            var shadeText = UiKit.Label(card.Shade.transform, "미보유 · 이번 턴 매매 불가", 14, Palette.SubText, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(shadeText.rectTransform);
            card.Shade.gameObject.SetActive(false);
            if (State.phase == Protocol.Investment) BuildLiveControls(r, card, id);
            if (State.stockSystem) {
                card.History = UiKit.Label(r, "", 10, Palette.SubText, TextAnchor.UpperLeft);
                card.History.name = "SegmentHistory_" + id;
                card.History.rectTransform.anchorMin = Vector2.zero; card.History.rectTransform.anchorMax = new Vector2(1, 0);
                card.History.rectTransform.offsetMin = new Vector2(8, State.phase == Protocol.Investment ? 44 : 4);
                card.History.rectTransform.offsetMax = new Vector2(-8, State.phase == Protocol.Investment ? 84 : 44);
                card.SegmentLabels = new Text[6];
                for (int segment = 0; segment < 6; segment++) {
                    var label = UiKit.Label(card.History.transform, "", 9, Palette.SubText, TextAnchor.UpperLeft);
                    label.name = "SegmentTokens_" + id + "_" + segment;
                    UiKit.Anchor(label.rectTransform, segment / 6f, 0, (segment + 1) / 6f, 1, 1, 0);
                    card.SegmentLabels[segment] = label;
                }
                card.Probability = UiKit.Label(r, "", 10, Palette.Text, TextAnchor.UpperLeft);
                card.Probability.name = "ReactionProbability_" + id;
                card.Probability.rectTransform.anchorMin = Vector2.zero; card.Probability.rectTransform.anchorMax = new Vector2(1, 0);
                card.Probability.rectTransform.offsetMin = new Vector2(8, State.phase == Protocol.Investment ? 86 : 46);
                card.Probability.rectTransform.offsetMax = new Vector2(-8, State.phase == Protocol.Investment ? 114 : 76);
                UiKit.Stretch(chartRoot, 8, 53, 8, State.phase == Protocol.Investment ? 118 : 80);
                return;
            }
            if (State.phase != Protocol.Selection && State.phase != Protocol.Investment) return;
            card.Inventory = UiKit.Rect(r, "ShareInventory_" + id);
            card.Inventory.anchorMin = Vector2.zero; card.Inventory.anchorMax = new Vector2(1, 0);
            float inventoryBottom = State.phase == Protocol.Investment ? 82 : 22;
            card.Inventory.offsetMin = new Vector2(8, inventoryBottom); card.Inventory.offsetMax = new Vector2(-8, inventoryBottom + 22);
            card.Units = new Image[StockRules.Supply];
            for (int i = 0; i < card.Units.Length; i++)
            {
                card.Units[i] = UiKit.Panel(card.Inventory, "ShareUnit_" + id + "_" + i, Palette.Disabled);
                UiKit.Anchor(card.Units[i].rectTransform, i / (float)StockRules.Supply, 0, (i + 1) / (float)StockRules.Supply, 1, 1, 1);
                card.Units[i].raycastTarget = false;
            }
            // 5칸마다 구분선과 숫자(5·10·15·20)
            for (int k = 5; k <= StockRules.Supply; k += 5)
            {
                float x = k / (float)StockRules.Supply;
                if (k < StockRules.Supply)
                {
                    var sep = UiKit.Panel(card.Inventory, "ShareMark_" + k, Palette.Text);
                    sep.rectTransform.anchorMin = new Vector2(x, 0); sep.rectTransform.anchorMax = new Vector2(x, 1);
                    sep.rectTransform.offsetMin = new Vector2(-1, -3); sep.rectTransform.offsetMax = new Vector2(1, 3);
                    sep.raycastTarget = false;
                }
                var mark = UiKit.Label(card.Inventory, k.ToString(), 11, Palette.SubText, TextAnchor.UpperCenter, FontStyle.Bold);
                mark.name = "ShareMarkLabel_" + k;
                UiKit.Place(mark.rectTransform, new Vector2(x, 0), new Vector2(k == StockRules.Supply ? 1f : .5f, 1), new Vector2(0, -3), new Vector2(26, 14));
            }
            // 투자 결정에서는 실시간 현황만 보여 주고 매수 슬라이더는 만들지 않는다.
            if (State.phase == Protocol.Investment) return;
            card.Stamp = UiKit.Label(r, "매진", 30, new Color32(0xFF, 0xFF, 0xFF, 0xD0), TextAnchor.MiddleCenter, FontStyle.Bold);
            card.Stamp.name = "SoldOutStamp_" + id;
            UiKit.Place(card.Stamp.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 6), new Vector2(140, 50));
            card.Stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 12);
            card.Stamp.gameObject.SetActive(false);
            card.SliderRoot = UiKit.Rect(card.Inventory, "ShareSlider_" + id);
            var hit = UiKit.Panel(card.SliderRoot, "HitArea", Color.clear, raycast: true); UiKit.Stretch(hit.rectTransform, 0, -4, 0, -4);
            var handle = UiKit.Panel(card.SliderRoot, "Handle", Palette.Text, rounded: true, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(5, 4);
            card.Count = UiKit.Label(handle.transform, "", 11, Palette.Text, TextAnchor.LowerCenter, FontStyle.Bold);
            card.Count.name = "ShareCount_" + id;
            UiKit.Place(card.Count.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(54, 14));
            var trigger = card.SliderRoot.gameObject.AddComponent<EventTrigger>();
            card.Slider = card.SliderRoot.gameObject.AddComponent<Slider>(); card.Slider.minValue = 0; card.Slider.wholeNumbers = true;
            card.Slider.handleRect = handle.rectTransform; card.Slider.targetGraphic = handle; card.Slider.transition = Selectable.Transition.None;
            var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            down.callback.AddListener(_ => { if (Editable) draggingCompany = id; }); trigger.triggers.Add(down);
            var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            up.callback.AddListener(_ => { draggingCompany = null; Commit(id); }); trigger.triggers.Add(up);
            card.Slider.onValueChanged.AddListener(value => {
                if (!Editable) return;
                int own = Holding(id), other = OtherShares(id);
                int cap = Math.Min(Portfolio.LimitFor(id), Math.Min(20 - other, own + Math.Min(Portfolio.draftSharesRemaining, Portfolio.CashSlots)));
                targets[id] = Math.Max(0, Math.Min(Mathf.RoundToInt(value), cap));
                card.Slider.SetValueWithoutNotify(targets[id]); RefreshCards(false);
                if (draggingCompany != id) Commit(id);
            });
        }
        private int OtherShares(string id) => State.shareMarket?.FirstOrDefault(c => c.companyId == id)?.owners?.Where(v => v.playerId != controller.LocalPlayerId)
            .Sum(v => v.shares + v.reserved) ?? 0;
        private void BuildLiveControls(RectTransform parent, Card card, string id)
        {
            var controls = UiKit.Rect(parent, "LiveOrders_" + id);
            controls.anchorMin = Vector2.zero; controls.anchorMax = new Vector2(1, 0);
            controls.offsetMin = new Vector2(4, 2); controls.offsetMax = new Vector2(-4, 42);
            var tokenRow = UiKit.Rect(controls, "TokenSquares_" + id);
            tokenRow.anchorMin = new Vector2(0, .65f); tokenRow.anchorMax = Vector2.one;
            tokenRow.offsetMin = Vector2.zero; tokenRow.offsetMax = Vector2.zero;
            card.TokenSquares = new Image[StockRules.Slots];
            for (int t = 0; t < card.TokenSquares.Length; t++) {
                var sq = UiKit.Panel(tokenRow, "TokenSquare_" + id + "_" + t, Color.white);
                sq.raycastTarget = false;
                float cx = (t - 1) * 18f;
                UiKit.Place(sq.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(cx, 0), new Vector2(12, 12));
                sq.gameObject.SetActive(false);
                card.TokenSquares[t] = sq;
            }
            card.Actions = new Button[2];
            string[] labels = { "Buy", "Sell" };
            for (int i = 0; i < labels.Length; i++)
            {
                int action = i;
                var button = UiKit.Button(controls, labels[i], 12, Palette.AccentSoft, Palette.Accent, () =>
                {
                    var h = Portfolio.holdings.First(v => v.companyId == id);
                    card.LiveTradeDirection = action == 0 ? 1 : -1;
                    RefreshCards(false);
                    controller.SetStockPosition(id, h.activeShares + (action == 0 ? 1 : -1));
                });
                button.name = "LiveOrder_" + id + "_" + i;
                card.Actions[i] = button;
                UiKit.Anchor((RectTransform)button.transform, i / 2f, 0, (i + 1) / 2f, .62f, 2, 0);
            }
        }

        private void Commit(string id)
        {
            if (Editable && targets[id] != Holding(id)) controller.TradeShares(new[] { new ShareOrder { companyId = id, targetShares = targets[id] } });
            else RefreshCards();
        }
        private void RefreshCards(bool synchronize = true)
        {
            if (rumors != null) UiKit.SetButton(rumors, controller.Store.RumorsVisible ? "찌라시 숨기기" : "찌라시 보이기", true, Palette.AccentSoft, Palette.Text);
            bool pending = controller.Store.HasPending(StockRules.Trade);
            if (synchronize && !pending && draggingCompany == null)
                foreach (var id in UiRules.CompanyIds) targets[id] = Holding(id);
            if (summary != null) summary.text = State.phase == Protocol.Selection
                ? (Editable ? "내 차례 · 남은 매수 " + Portfolio.draftSharesRemaining + "/15주" : pending ? "매수 처리 중" : State.currentPlayerId + " 차례") + " · 보유 " + Portfolio.usedSlots + "/30주 · 현금 " + Portfolio.CashSlots + "주"
                : State.phase == Protocol.Info ? "정보 공유 2분 · 왼쪽 플레이어에 마우스를 올려 카드 / 계획을 보내세요."
                : State.phase == Protocol.Investment ? (State.stockSegmentIndex + 1) + "/6구간 · " + (State.stockSegmentRevealing ? "시장 공개 · 배치 변경은 다음 구간에" : "비공개 선택 10초 · 미사용 투자말 " + Portfolio.CashSlots + "/3 · 시간 종료 시 현재 배치 유지")
                : "확정된 기업별 주식 현황 · 차트를 눌러 확인하세요.";
            foreach (var pair in cards)
            {
                string id = pair.Key; var card = pair.Value;
                var company = State.shareMarket?.FirstOrDefault(c => c.companyId == id);
                var owners = company?.owners ?? Array.Empty<ShareOwner>();
                int used = owners.Sum(o => o.shares + o.reserved), total = company?.totalShares ?? StockRules.Supply;
                string tag = used >= total ? "  <color=#F2E6CC><b>매진</b></color>" : total - used <= 4 ? "  <color=#F2E6CC>품절 임박</color>" : "";
                card.Heading.text = "<b>" + id + "</b>" + (State.phase == Protocol.Info ? id == "FOOD" || id == "AUTO" ? " · 기본 흐름" : " · 시장 반응" : "");
                // 진입·청산 단계: 내가 보유한(매매 가능한) 기업 강조, 미보유는 흐리게
                bool investing = State.phase == Protocol.Investment && !State.stockSegmentRevealing && !controller.Store.OrderLocked;
                int mine = Portfolio?.holdings?.Where(h => h.companyId == id).Sum(h => h.shares) ?? 0;
                int availableToBuy = Portfolio.AvailableToBuy(company);
                bool tradable = investing && (mine > 0 || availableToBuy > 0);
                if (card.Actions != null) {
                    int active = Portfolio.holdings.First(h => h.companyId == id).activeShares;
                    int free = availableToBuy;
                    if (card.TokenSquares != null) {
                        var playerColor = Palette.Player(controller.LocalPlayerId);
                        for (int t = 0; t < card.TokenSquares.Length; t++) {
                            card.TokenSquares[t].color = playerColor;
                            card.TokenSquares[t].gameObject.SetActive(t < active);
                        }
                    }
                    bool enabled = tradable && !State.paused && !controller.Store.HasPending(StockRules.Position);
                    bool[] available = { free > 0, active > 0 };
                    for (int i = 0; i < available.Length; i++) card.Actions[i].interactable = enabled && available[i];
                }
                // 매매 가능: 네모 테두리·보유 주 네모 배지 대신 카드 왼쪽 위 흰 점만(보유 수는 아래 '보유 N' 줄에 있음)
                int draft = card.InitialShares;
                bool hasDraft = targets.TryGetValue(id, out draft);
                bool draftChanged = card.Slider != null && hasDraft && draft != card.InitialShares;
                bool liveChanged = card.Slider == null && card.LiveTradeDirection != 0;
                bool changed = draftChanged || liveChanged;
                bool draftBuy = draftChanged ? draft > card.InitialShares : card.LiveTradeDirection > 0;
                Color draftColor = draftBuy ? Palette.Good : Palette.Bad;
                card.Frame.enabled = changed;
                card.Frame.effectColor = draftColor;
                card.Frame.effectDistance = changed ? new Vector2(4, -4) : Vector2.zero;
                // 매수·매도 상태는 테두리와 방향 표시로만 강조합니다. 카드 확대는 레이아웃을 밀 수 있어 사용하지 않습니다.
                if (card.TradeMarker != null)
                {
                    card.TradeMarker.gameObject.SetActive(changed);
                    card.TradeMarker.text = draftBuy ? "▲ 매수" : "▼ 매도";
                    card.TradeMarker.color = draftColor;
                }
                card.MineBadge.gameObject.SetActive(false);
                card.TradableDot.gameObject.SetActive(tradable);
                card.Shade.gameObject.SetActive(false);
                card.Heading.rectTransform.offsetMin = new Vector2(tradable ? 22 : 10, card.Heading.rectTransform.offsetMin.y);
                card.Heading.rectTransform.offsetMax = new Vector2(-70, card.Heading.rectTransform.offsetMax.y);
                bool showNextArrows = State.phase == Protocol.Investment && State.stockSegmentIndex >= 0 && State.stockSegmentIndex < 6;
                card.Owners.text = State.phase == Protocol.Investment ? "" : string.Join("  ", owners.Select(o => "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Player(o.playerId)) + ">" + o.playerId + " ■" + (o.shares + o.reserved) + "</color>"));
                if (card.SegmentPointerRoot != null) {
                    card.SegmentPointerRoot.gameObject.SetActive(showNextArrows);
                    if (showNextArrows) {
                        int startDay = State.stockSegmentIndex * 5 + 1;
                        int endDay = startDay + 4;
                        float x0 = (startDay + 0.5f) / 31f;
                        float x1 = (endDay + 0.5f) / 31f;
                        card.SegmentSpanLine.anchorMin = new Vector2(x0, 0.68f);
                        card.SegmentSpanLine.anchorMax = new Vector2(x1, 0.80f);
                        card.SegmentSpanLine.offsetMin = Vector2.zero;
                        card.SegmentSpanLine.offsetMax = Vector2.zero;
                        UiKit.Place(card.SegmentStartArrow, new Vector2(x0, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(18, 16));
                        UiKit.Place(card.SegmentEndArrow, new Vector2(x1, 0), new Vector2(.5f, 0), Vector2.zero, new Vector2(18, 16));
                    }
                }
                if (card.History != null) {
                    var history = State.stockSegmentHistory ?? Array.Empty<StockSegmentRecord>();
                    for (int segment = 0; segment < 6; segment++) {
                        var record = history.FirstOrDefault(s => s.segmentIndex == segment);
                        var segmentOwners = record?.companies.FirstOrDefault(c => c.companyId == id)?.owners ?? Array.Empty<ShareOwner>();
                        string tokens = string.Join(" ", segmentOwners.Where(o => o.shares > 0)
                            .Select(o => "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Player(o.playerId)) + ">" + new string('■', o.shares) + "</color>"));
                        card.SegmentLabels[segment].text = (segment + 1) + "구간\n" + (record == null ? "미공개" : tokens.Length == 0 ? "없음" : tokens);
                    }
                }
                if (card.Probability != null) card.Probability.text = BoardChartAnnotations.ProbabilityLabel(State, Portfolio, id);
                var visible = controller.Store.View?.companies?.FirstOrDefault(c => c.companyId == id);
                var revealed = controller.Store.RevealedTurn == State.turn ? controller.Store.Revealed?.FirstOrDefault(c => c.companyId == id) : null;
                card.Chart.SetCompany(id);
                card.Chart.SetSegmentKnowledge(State.segmentKnowledge, id);
                card.Chart.SetData(DailyBarModel.Build(visible, revealed));
                card.Chart.SetTrendThroughDay(revealed != null ? 30 : State.phase == Protocol.Investment ? Portfolio?.processedDay : (int?)null);
                card.Chart.SetCurrentDay(StockRules.CurrentDay(State, Portfolio));
                // 체결 표시는 확대 차트에서만 보여 주어 작은 카드 차트가 복잡해지지 않게 합니다.
                card.Chart.SetFills(null);
                card.Chart.ShowTradeLabels = false; // 예약 구간은 띠만(시가·종가 글자 없음)
                if (State.phase == Protocol.Investment || revealed != null) {
                    int? close = InformationWindowRules.KnownDays(visible)[Portfolio.processedDay];
                    card.Heading.text = "<b>" + id + "</b>  " + (close.HasValue ? (10000 + close.Value).ToString("N0") : "—");
                }
                if (revealed != null) card.Chart.ClearTradeOverlays(); // 시장 공개·투자 결과: 진입·청산·수익률 표시 없이 주가만
                else card.Chart.SetTradeIntervals(Array.Empty<Interval>());
                card.Chart.SetPlanOverlays(BoardChartAnnotations.Build(controller.Store.TradePlans, controller.Store.View, id, controller.LocalPlayerId, controller.Store.RumorsVisible));
                if (card.Units == null) continue;
                var colors = owners.Where(o => o.playerId != controller.LocalPlayerId).OrderBy(o => o.playerId)
                    .SelectMany(o => Enumerable.Repeat(Palette.Player(o.playerId), Math.Max(0, o.shares + o.reserved))).ToList();
                int other = Math.Min(20, colors.Count);
                int ownUnits = card.Slider != null ? targets[id] : owners.Where(o => o.playerId == controller.LocalPlayerId).Sum(o => o.shares + o.reserved);
                colors.AddRange(Enumerable.Repeat(Palette.Player(controller.LocalPlayerId), Math.Max(0, ownUnits)));
                Color empty = new Color32(0x40, 0x35, 0x27, 0xFF);
                bool first = card.LastColors == null;
                if (first) card.LastColors = new Color[card.Units.Length];
                for (int i = 0; i < card.Units.Length; i++)
                {
                    var c = i < colors.Count ? colors[i] : empty;
                    card.Units[i].color = c; card.LastColors[i] = c;
                }
                if (card.Slider == null) continue;
                bool soldOut = used >= total;
                card.Stamp.gameObject.SetActive(soldOut);
                if (soldOut && !card.WasSoldOut && !first) UiAnim.Pulse(card.Stamp.rectTransform, 0.1f, 1.35f);
                card.WasSoldOut = soldOut;
                card.Count.text = targets[id] > 0 ? "내 " + targets[id] + "주" : "";
                card.Count.color = Palette.Player(controller.LocalPlayerId);
                card.SliderRoot.anchorMin = new Vector2(other / 20f, 0); card.SliderRoot.anchorMax = Vector2.one;
                card.SliderRoot.offsetMin = Vector2.zero; card.SliderRoot.offsetMax = Vector2.zero;
                card.Slider.maxValue = Math.Max(1, 20 - other); card.Slider.SetValueWithoutNotify(targets[id]);
                card.Slider.interactable = Editable && other < 20 && (Holding(id) > 0 || Portfolio.draftSharesRemaining > 0 && Portfolio.CashSlots > 0);
            }
            if (finish != null) finish.interactable = Editable;
        }
        private void BuildAugments()
        {
            Label(root, "증강 선택 · 1·4턴에 하나씩, 최대 2개 선택합니다", 0, 40, 20);
            Label(root, "선택한 증강: " + string.Join(" / ", Portfolio.augments.Select(StockRules.Name)), 42, 35, 13);
            if (Portfolio.augmentChosen) { Label(root, "선택 완료 · 다른 플레이어를 기다리는 중입니다.", 100, 100, 18); return; }
            var choices = (Portfolio.candidates ?? new string[0]).Where(c => !string.IsNullOrEmpty(c)).ToArray();
            // 후보 수만큼 칸을 나눈다(3칸 고정이면 후보가 2개일 때 한 칸이 비어 보임)
            float columns = Mathf.Max(1, choices.Length);
            for (int i = 0; i < choices.Length; i++)
            {
                string id = choices[i];
                // 카드의 위쪽 위치는 유지하고 아래쪽 여백만 줄여 한 줄 정도 확장합니다.
                // 따라서 상단 안내 문구는 가리지 않으면서 설명·이미지·버튼에 아래 공간을 제공합니다.
                var cell = UiKit.Rect(root, "Augment_" + id); UiKit.Anchor(cell, i / columns, 0, (i + 1) / columns, 1, 6, 90);
                cell.offsetMin = new Vector2(cell.offsetMin.x, 30);
                var panel = UiKit.Panel(cell, "Card", Palette.Card, rounded: true); UiKit.Stretch(panel.rectTransform);
                UiKit.AddFrame(panel, Palette.CardBorder);
                // 증강 이름은 규칙 카탈로그의 원래 표기를 그대로 사용합니다.
                // HIDDEN 손실 제어도 다른 증강 이름과 마찬가지로 한 줄로 표시합니다.
                var name = UiKit.Label(panel.transform, StockRules.Name(id), 20, Palette.Accent, TextAnchor.UpperCenter, FontStyle.Bold); UiKit.Stretch(name.rectTransform, 12, 20, 12, 0);
                var description = UiKit.Label(panel.transform, StockRules.Description(id), 15, Palette.Text, TextAnchor.UpperLeft);
                // 아래쪽으로 확장된 카드 안에서 설명 영역의 아래쪽을 이미지 바로 위까지 확장합니다.
                // 특히 HIDDEN 설명은 문장이 길기 때문에 이미지와의 여백을 더 줄여 세로 공간을 확보합니다.
                // 글자 크기는 다른 설명과 크게 차이 나지 않도록 13~15 범위에서만 자동 조정합니다.
                // 문장을 강제로 잘라내지 않고 모두 표시하여 설명의 의미가 사라지지 않게 합니다.
                float descriptionBottom = id == "HIDDEN_LOSS_CONTROL" ? 205 : 220;
                UiKit.Stretch(description.rectTransform, 16, 64, 16, descriptionBottom);
                description.resizeTextForBestFit = true;
                description.resizeTextMinSize = 13;
                description.resizeTextMaxSize = 15;
                description.verticalOverflow = VerticalWrapMode.Overflow;
                var artwork = AugmentArtwork.Create(panel.transform, id, 150);
                UiKit.Place(artwork, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 60), new Vector2(150, 150));
                var button = UiKit.Button(panel.transform, "이 증강 선택", 14, Palette.Accent, Color.white, () => controller.ChooseAugment(id, focus));
                button.name = "ChooseAugment_" + id;
                UiKit.Place((RectTransform)button.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(160, 36));
                button.interactable = !State.paused && !controller.Store.HasPending(StockRules.Choose);
                // 증강 후보가 처음 뜰 때 카드가 한 장씩 뒤집히며 공개
                UiAnim.FlipIn(cell, "augment:" + State.turn + ":" + string.Join(",", choices) + ":" + i, 0.05f + i * 0.1f, 0.28f); // 빠르게 뒤집기(0.55초→0.28초, 카드 간격 0.22초→0.1초)
            }
        }
        private static Text Label(RectTransform parent, string text, float top, float height, int size = 14)
        {
            var t = UiKit.Label(parent, text, size, Palette.Text, TextAnchor.MiddleLeft);
            t.rectTransform.anchorMin = new Vector2(0, 1); t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(4, -top - height); t.rectTransform.offsetMax = new Vector2(-4, -top);
            return t;
        }
    }
}
