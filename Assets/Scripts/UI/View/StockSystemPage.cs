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
            public Text Heading, Owners;
            public BarChartGraphic Chart;
            public RectTransform Inventory, SliderRoot;
            public Slider Slider;
            public Image[] Units;
        }
        private readonly RectTransform root;
        private readonly GameClientController controller;
        private readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        private readonly Dictionary<string, int> targets = new Dictionary<string, int>();
        private string builtKey, draggingCompany, focus = "AI";
        private Text summary;
        private Button finish;
        public Action<string> OpenPlan;
        public StockSystemPage(RectTransform parent, GameClientController controller) { root = parent; this.controller = controller; }
        private PublicState State => controller.Store.Public;
        private PortfolioState Portfolio => controller.Store.View?.portfolio;
        private string BuildKey => State.turn + ":" + State.phase + ":" + State.selectionIndex + ":" + State.currentPlayerId;
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
            BuildOrderStrip();
            summary = Label(root, "", 49, 29, 13); summary.name = "StockPurchaseSummary";
            var grid = UiKit.Rect(root, "ShareMarketGrid"); UiKit.Stretch(grid, 0, 82, 0, 44);
            for (int i = 0; i < UiRules.CompanyIds.Length; i++)
            {
                string id = UiRules.CompanyIds[i];
                var cell = UiKit.Rect(grid, "ShareCard_" + id);
                UiKit.Anchor(cell, i % 3 / 3f, i < 3 ? .5f : 0, (i % 3 + 1) / 3f, i < 3 ? 1 : .5f, 4, 4);
                BuildCompany(cell, id);
            }
            if (State.phase == Protocol.Selection)
            {
                finish = UiKit.Button(root, "매수 완료 · 다음 차례", 14, Palette.Accent, Color.white, () => controller.PassStockDraft());
                finish.name = "PassShareDraft";
                UiKit.Place((RectTransform)finish.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-4, 3), new Vector2(190, 34));
                var help = Label(root, "회색 물량을 내 색으로 채우고 놓으면 바로 매수 · 차트 클릭으로 확대", 0, 0, 12);
                help.rectTransform.anchorMin = Vector2.zero; help.rectTransform.anchorMax = new Vector2(1, 0);
                help.rectTransform.offsetMin = new Vector2(4, 3); help.rectTransform.offsetMax = new Vector2(-202, 35);
                if (Portfolio.augments.Contains("PRIORITY") && !Portfolio.priorityUsed)
                {
                    var priority = UiKit.Button(root, "선점 사용", 12, Palette.AccentSoft, Palette.Accent, () => controller.UseDraftPriority());
                    priority.name = "UseDraftPriority";
                    UiKit.Place((RectTransform)priority.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(4, 3), new Vector2(110, 34));
                    int next = Array.FindIndex(State.draftOrder ?? Array.Empty<string>(), Math.Min(State.selectionIndex + 1, State.draftOrder?.Length ?? 0), id => id == controller.LocalPlayerId);
                    priority.interactable = !State.paused && next > State.selectionIndex + 1 && !controller.Store.HasPending(StockRules.Priority);
                    help.rectTransform.offsetMin = new Vector2(124, 3);
                }
            }
            else if (State.phase == Protocol.Investment)
            {
                finish = UiKit.Button(root, "진입·청산 구간 확정", 14, Palette.Accent, Color.white,
                    () => controller.SubmitInvestment(controller.CurrentTradeReservations()));
                finish.name = "ConfirmStockIntervals";
                UiKit.Place((RectTransform)finish.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-4, 3), new Vector2(220, 34));
                var hint = Label(root, "차트를 눌러 예약 확인·수정 · 미설정 기업은 전체 기간 보유", 0, 0, 12);
                hint.rectTransform.anchorMin = Vector2.zero; hint.rectTransform.anchorMax = new Vector2(1, 0);
                hint.rectTransform.offsetMin = new Vector2(4, 3); hint.rectTransform.offsetMax = new Vector2(-230, 35);
            }
            else finish = null;
            RefreshCards();
        }
        private void BuildOrderStrip()
        {
            var ids = State.draftOrder ?? Array.Empty<string>();
            int first = Math.Min(StockRules.DraftRounds - 1, State.selectionIndex / 4) * 4;
            for (int i = first; i < Math.Min(ids.Length, first + 4); i++)
            {
                bool current = i == State.selectionIndex && State.phase == Protocol.Selection;
                var panel = UiKit.Panel(root, "StockDraftSlot_" + i, current ? Palette.AccentSoft : Palette.Card, rounded: true);
                panel.rectTransform.anchorMin = new Vector2((i - first) / 4f, 1); panel.rectTransform.anchorMax = new Vector2((i - first + 1) / 4f, 1);
                panel.rectTransform.offsetMin = new Vector2(3, -45); panel.rectTransform.offsetMax = new Vector2(-3, 0);
                var text = UiKit.Label(panel.transform, ids[i] + "  <size=12>" + (i / 4 + 1) + (State.developerMode ? "차 · 완료 시 진행" : "차 · 7초") + (current ? " · 지금" : "") + "</size>", 22, Palette.Player(ids[i]), TextAnchor.MiddleCenter, FontStyle.Bold);
                text.name = "StockDraftPlayer_" + i; UiKit.Stretch(text.rectTransform, 4, 1, 4, 1);
            }
        }
        private void BuildCompany(RectTransform cell, string id)
        {
            var panel = UiKit.Panel(cell, "ShareCardBackground", Palette.Card, rounded: true); UiKit.Stretch(panel.rectTransform);
            var r = panel.rectTransform;
            var card = new Card(); cards[id] = card;
            var expand = UiKit.Button(r, "", 14, Palette.Card, Palette.Text, () => OpenPlan?.Invoke(id));
            expand.name = "ExpandShare_" + id;
            UiKit.Place((RectTransform)expand.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -15), new Vector2(240, 26));
            card.Heading = expand.GetComponentInChildren<Text>();
            card.Owners = Label(r, "", 29, 20, 12); card.Owners.name = "ShareOwners_" + id;
            card.Owners.horizontalOverflow = HorizontalWrapMode.Overflow;
            var chartRoot = UiKit.Rect(r, "TradeChart_" + id); UiKit.Stretch(chartRoot, 8, 53, 8, id == "BANK" || State.phase != Protocol.Selection ? 8 : 38);
            card.Chart = chartRoot.gameObject.AddComponent<BarChartGraphic>(); card.Chart.SetInteractive(true);
            UiKit.Click(chartRoot.gameObject, () => OpenPlan?.Invoke(id));
            if (id == "BANK" || State.phase != Protocol.Selection) return;
            card.Inventory = UiKit.Rect(r, "ShareInventory_" + id);
            card.Inventory.anchorMin = Vector2.zero; card.Inventory.anchorMax = new Vector2(1, 0);
            card.Inventory.offsetMin = new Vector2(8, 8); card.Inventory.offsetMax = new Vector2(-8, 30);
            card.Units = new Image[StockRules.Supply];
            for (int i = 0; i < card.Units.Length; i++)
            {
                card.Units[i] = UiKit.Panel(card.Inventory, "ShareUnit_" + id + "_" + i, Palette.Disabled);
                UiKit.Anchor(card.Units[i].rectTransform, i / 20f, 0, (i + 1) / 20f, 1, 1, 1);
            }
            card.SliderRoot = UiKit.Rect(card.Inventory, "ShareSlider_" + id);
            var hit = UiKit.Panel(card.SliderRoot, "HitArea", Color.clear, raycast: true); UiKit.Stretch(hit.rectTransform, 0, -4, 0, -4);
            var handle = UiKit.Panel(card.SliderRoot, "Handle", Palette.Text, rounded: true, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(5, 4);
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
        private void Commit(string id)
        {
            if (Editable && targets[id] != Holding(id)) controller.TradeShares(new[] { new ShareOrder { companyId = id, targetShares = targets[id] } });
            else RefreshCards();
        }
        private void RefreshCards(bool synchronize = true)
        {
            bool pending = controller.Store.HasPending(StockRules.Trade);
            if (synchronize && !pending && draggingCompany == null)
                foreach (var id in UiRules.CompanyIds) targets[id] = Holding(id);
            if (summary != null) summary.text = State.phase == Protocol.Selection
                ? (Editable ? "내 차례 · 남은 매수 " + Portfolio.draftSharesRemaining + "/15주" : pending ? "매수 처리 중" : State.currentPlayerId + " 차례") + " · 보유 " + Portfolio.usedSlots + "/30주 · BANK " + Portfolio.CashSlots + "주"
                : State.phase == Protocol.Info ? "정보 탐색 중 구간 예약 가능 · 차트를 누르면 크게 볼 수 있습니다."
                : State.phase == Protocol.Investment ? "예약 구간을 확인하고 확정하세요. BANK 구간 밖의 미투자 수량은 현금으로 유지됩니다."
                : "확정된 기업별 주식 현황 · 차트를 눌러 확인하세요.";
            foreach (var pair in cards)
            {
                string id = pair.Key; var card = pair.Value;
                var company = State.shareMarket?.FirstOrDefault(c => c.companyId == id);
                var owners = company?.owners ?? Array.Empty<ShareOwner>();
                int used = owners.Sum(o => o.shares + o.reserved), total = company?.totalShares ?? (id == "BANK" ? StockRules.BankSupply : StockRules.Supply);
                card.Heading.text = id + " · " + used + "/" + total + "주  <size=11>확대 ↗</size>";
                card.Owners.text = string.Join("  ", owners.Select(o => "<color=#" + ColorUtility.ToHtmlStringRGB(Palette.Player(o.playerId)) + ">" + o.playerId + " ■" + (o.shares + o.reserved) + "</color>"));
                var visible = controller.Store.View?.companies?.FirstOrDefault(c => c.companyId == id);
                var revealed = controller.Store.RevealedTurn == State.turn ? controller.Store.Revealed?.FirstOrDefault(c => c.companyId == id) : null;
                card.Chart.SetData(DailyBarModel.Build(visible, revealed));
                if (revealed?.investments != null) card.Chart.SetRevealedInvestments(revealed.investments);
                else card.Chart.SetTradeIntervals(controller.Store.TradePlans.ChartIntervals(controller.Store.View, State.turn, id, controller.Store.OrderLocked), !controller.Store.OrderLocked);
                if (card.Slider == null) continue;
                var colors = owners.Where(o => o.playerId != controller.LocalPlayerId).SelectMany(o => Enumerable.Repeat(Palette.Player(o.playerId), Math.Max(0, o.shares + o.reserved))).ToList();
                int other = Math.Min(20, colors.Count);
                colors.AddRange(Enumerable.Repeat(Palette.Player(controller.LocalPlayerId), Math.Max(0, targets[id])));
                for (int i = 0; i < card.Units.Length; i++) card.Units[i].color = i < colors.Count ? colors[i] : new Color32(185, 190, 199, 255);
                card.SliderRoot.anchorMin = new Vector2(other / 20f, 0); card.SliderRoot.anchorMax = Vector2.one;
                card.SliderRoot.offsetMin = Vector2.zero; card.SliderRoot.offsetMax = Vector2.zero;
                card.Slider.maxValue = Math.Max(1, 20 - other); card.Slider.SetValueWithoutNotify(targets[id]);
                card.Slider.interactable = Editable && other < 20 && (Holding(id) > 0 || Portfolio.draftSharesRemaining > 0 && Portfolio.CashSlots > 0);
            }
            if (finish != null)
            {
                finish.interactable = State.phase == Protocol.Selection ? Editable : !State.paused && !controller.Store.OrderLocked && !controller.Store.HasPending(CommandTypes.SubmitInvestment);
                if (State.phase == Protocol.Investment) UiKit.SetButton(finish, controller.Store.OrderLocked ? "구간 확정 완료 · 대기" : "진입·청산 구간 확정", finish.interactable, Palette.Accent, Color.white);
            }
        }
        private void BuildAugments()
        {
            Label(root, "증강 선택 · 1·3·5턴에 하나씩 선택합니다", 0, 40, 20);
            Label(root, "선택한 증강: " + string.Join(" / ", Portfolio.augments.Select(StockRules.Name)), 42, 35, 13);
            if (Portfolio.augmentChosen) { Label(root, "선택 완료 · 다른 플레이어를 기다리는 중입니다.", 100, 100, 18); return; }
            var choices = Portfolio.candidates ?? new string[0];
            for (int i = 0; i < choices.Length; i++)
            {
                string id = choices[i];
                var cell = UiKit.Rect(root, "Augment_" + id); UiKit.Anchor(cell, i / 3f, 0, (i + 1) / 3f, 1, 6, 90);
                var panel = UiKit.Panel(cell, "Card", Palette.Card, rounded: true); UiKit.Stretch(panel.rectTransform);
                var name = UiKit.Label(panel.transform, StockRules.Name(id), 20, Palette.Accent, TextAnchor.UpperCenter, FontStyle.Bold); UiKit.Stretch(name.rectTransform, 12, 20, 12, 0);
                var description = UiKit.Label(panel.transform, StockRules.Description(id), 15, Palette.Text, TextAnchor.UpperLeft); UiKit.Stretch(description.rectTransform, 16, 64, 16, 64);
                var button = UiKit.Button(panel.transform, "이 증강 선택", 14, Palette.Accent, Color.white, () => controller.ChooseAugment(id, focus));
                button.name = "ChooseAugment_" + id;
                UiKit.Place((RectTransform)button.transform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 14), new Vector2(160, 36));
                button.interactable = !State.paused && !controller.Store.HasPending(StockRules.Choose);
            }
            var targetLabel = Label(root, "집중 투자자 적용 기업: " + focus, 0, 0, 12);
            targetLabel.rectTransform.anchorMin = Vector2.zero; targetLabel.rectTransform.anchorMax = new Vector2(1, 0);
            targetLabel.rectTransform.offsetMin = new Vector2(6, 45); targetLabel.rectTransform.offsetMax = new Vector2(-6, 70);
            for (int i = 0; i < UiRules.CompanyIds.Length; i++)
            {
                string id = UiRules.CompanyIds[i]; if (id == "BANK") continue; var b = UiKit.Button(root, id, 12, focus == id ? Palette.AccentSoft : Palette.Card, Palette.Accent,
                    () => { focus = id; targetLabel.text = "집중 투자자 적용 기업: " + focus; });
                b.name = "FocusCompany_" + id;
                UiKit.Place((RectTransform)b.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(6 + i * 90, 8), new Vector2(82, 30));
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
