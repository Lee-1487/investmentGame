using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    // Share targets are local previews. Only the apply button sends an atomic server order.
    public sealed class StockSystemPage
    {
        private readonly RectTransform root;
        private readonly GameClientController controller;
        private readonly Dictionary<string, int> targets = new Dictionary<string, int>();
        private readonly List<string> chosen = new List<string>();
        private readonly Dictionary<string, Text> quantityLabels = new Dictionary<string, Text>();
        private readonly Dictionary<string, Slider> sliders = new Dictionary<string, Slider>();
        private readonly Dictionary<string, Image> cardBackgrounds = new Dictionary<string, Image>();
        private string key, focus = "AI";
        private Text preview;
        private Button apply;
        public StockSystemPage(RectTransform parent, GameClientController controller) { root = parent; this.controller = controller; }
        private PublicState State => controller.Store.Public;
        private PortfolioState Portfolio => controller.Store.View?.portfolio;
        private int Holding(string id) => State.shareMarket?.FirstOrDefault(c => c.companyId == id)?.owners?.FirstOrDefault(v => v.playerId == controller.LocalPlayerId)?.shares
            ?? Portfolio?.holdings?.FirstOrDefault(v => v.companyId == id)?.shares ?? 0;
        private bool Editable => controller.CanDraftShares();
        public void Build()
        {
            quantityLabels.Clear(); sliders.Clear(); cardBackgrounds.Clear();
            var state = State; var p = Portfolio;
            if (p == null) { Label(root, "주식·증강 정보를 기다리고 있습니다.", 0, 80); return; }
            string next = state.turn + ":" + state.selectionIndex + ":" + state.currentPlayerId;
            if (key != next)
            { key = next; targets.Clear(); chosen.Clear(); foreach (var id in UiRules.CompanyIds) targets[id] = Holding(id); }
            if (state.phase == StockRules.AugmentPhase) { BuildAugments(); return; }
            bool draft = state.phase == Protocol.Selection;
            Label(root, draft ? (Editable ? "내 주식 확보 차례 · 목표 보유량을 정하고 적용하세요" : state.currentPlayerId + " 님이 주식을 조정하고 있습니다")
                : state.phase == Protocol.Info ? "정보 확인 후 증강·주식 드래프트가 이어집니다. 보유 주식은 다음 턴으로 이월됩니다."
                : state.phase == Protocol.Ended ? "6턴 게임 종료 · 최종 승자 " + (controller.Store.Scoreboard?.winnerPlayerId ?? "") : "확정 포트폴리오 · 주식 수에 따라 자산을 30칸으로 배분합니다.", 0, 30);
            BuildOrderStrip();
            var summary = "내 포트폴리오 " + p.usedSlots + "/30칸 · 현금 " + p.CashSlots + "칸 · 증강 " + string.Join(" / ", (p.augments ?? new string[0]).Select(StockRules.Name));
            Label(root, summary, 65, 28, 12);
            var grid = UiKit.Rect(root, "ShareMarketGrid"); UiKit.Stretch(grid, 0, 96, 0, 106);
            for (int i = 0; i < UiRules.CompanyIds.Length; i++)
            {
                var card = UiKit.Rect(grid, "ShareCard_" + UiRules.CompanyIds[i]);
                int row = i / 3, col = i % 3;
                UiKit.Anchor(card, col / 3f, row == 0 ? .5f : 0, (col + 1) / 3f, row == 0 ? 1 : .5f, 4, 4);
                BuildCompany(card, UiRules.CompanyIds[i], draft);
            }
            BuildPortfolio();
            if (draft)
            {
                apply = UiKit.Button(root, "목표 보유량 적용", 13, Palette.Accent, Color.white, () => {
                    if (Editable && chosen.Count > 0) controller.TradeShares(chosen.Select(id => new ShareOrder { companyId = id, targetShares = targets[id] }).ToArray());
                }); apply.name = "ApplyShareDraft";
                UiKit.Place((RectTransform)apply.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-4, 4), new Vector2(190, 34));
                var pass = UiKit.Button(root, "이번 차례 유지", 12, Palette.Card, Palette.Text, () => controller.PassStockDraft());
                pass.name = "PassShareDraft";
                UiKit.Place((RectTransform)pass.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-202, 4), new Vector2(130, 34)); pass.interactable = Editable;
                if (p.augments.Contains("PRIORITY") && !p.priorityUsed)
                {
                    var priority = UiKit.Button(root, "선점 사용 · 1회", 11, Palette.AccentSoft, Palette.Accent, () => controller.UseDraftPriority());
                    priority.name = "UseDraftPriority";
                    UiKit.Place((RectTransform)priority.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(4, 4), new Vector2(130, 34));
                    int nextSlot = Array.FindIndex(state.draftOrder ?? new string[0], Math.Min(state.selectionIndex + 1, state.draftOrder?.Length ?? 0), id => id == controller.LocalPlayerId);
                    priority.interactable = !state.paused && nextSlot > state.selectionIndex + 1 && !controller.Store.HasPending(StockRules.Priority);
                }
                preview = Label(root, "", 0, 0); preview.rectTransform.anchorMin = Vector2.zero; preview.rectTransform.anchorMax = new Vector2(1, 0);
                preview.rectTransform.offsetMin = new Vector2(4, 39); preview.rectTransform.offsetMax = new Vector2(-4, 63);
                UpdatePreview();
            }
            else
            {
                var board = controller.Store.Scoreboard;
                if (board?.turn == state.turn)
                    Label(root, string.Join("    ", board.rows.OrderBy(v => v.rank).Select(v => v.rank + "등 " + v.playerId + " " + DisplayFormat.Money(v.totalAssets) + " (" + DisplayFormat.Percent(v.turnReturnBps) + ")")), 0, 0).rectTransform.anchoredPosition = new Vector2(0, 8);
            }
        }
        private void BuildOrderStrip()
        {
            var ids = State.draftOrder ?? new string[0];
            for (int i = 0; i < ids.Length; i++)
            {
                var panel = UiKit.Panel(root, "StockDraftSlot_" + i, i == State.selectionIndex && State.phase == Protocol.Selection ? Palette.AccentSoft : Palette.Card, rounded: true);
                var r = panel.rectTransform; r.anchorMin = new Vector2(i / 8f, 1); r.anchorMax = new Vector2((i + 1) / 8f, 1);
                r.offsetMin = new Vector2(3, -62); r.offsetMax = new Vector2(-3, -32);
                var t = UiKit.Label(r, (i + 1) + " · " + ids[i], 12, Palette.Player(ids[i])); UiKit.Stretch(t.rectTransform);
            }
        }
        private void BuildCompany(RectTransform cell, string id, bool draft)
        {
            var bg = UiKit.Panel(cell, "ShareCardBackground", chosen.Contains(id) ? Palette.AccentSoft : Palette.Card, rounded: true); UiKit.Stretch(bg.rectTransform);
            cardBackgrounds[id] = bg;
            var r = bg.rectTransform;
            var company = State.shareMarket?.FirstOrDefault(v => v.companyId == id);
            var owners = company?.owners ?? new ShareOwner[0]; int used = owners.Sum(v => v.shares);
            var heading = UiKit.Button(r, id + " · " + used + "/20주 보유", 12, Palette.Card, Palette.Text, () => { SelectCompany(id); UpdatePreview(); });
            heading.name = "SelectShare_" + id; UiKit.Stretch((RectTransform)heading.transform, 4, 3, 4, 0); ((RectTransform)heading.transform).anchorMin = new Vector2(0, 1); ((RectTransform)heading.transform).offsetMin = new Vector2(4, -27);
            heading.interactable = draft && Editable;
            int slot = 0;
            foreach (var owner in owners)
                for (int n = 0; n < owner.shares; n++) { ShareBlock(r, id, slot++, Palette.Player(owner.playerId)); }
            while (slot < StockRules.Supply) ShareBlock(r, id, slot++, Palette.Disabled);
            for (int i = 0; i < owners.Length; i++)
            {
                var label = UiKit.Label(r, "■ " + owners[i].playerId + ": " + owners[i].shares + "주", 10, Palette.Player(owners[i].playerId), TextAnchor.MiddleLeft);
                label.name = "ShareOwner_" + id + "_" + owners[i].playerId;
                var lr = label.rectTransform; lr.anchorMin = new Vector2(i % 2 * .5f, 1); lr.anchorMax = new Vector2((i % 2 + 1) * .5f, 1);
                lr.offsetMin = new Vector2(6, -66 - (i / 2) * 16); lr.offsetMax = new Vector2(-2, -50 - (i / 2) * 16);
            }
            int cap = Portfolio.LimitFor(id);
            var quantity = UiKit.Label(r, "", 10, Palette.Text, TextAnchor.MiddleCenter); quantity.name = "ShareTarget_" + id;
            quantity.rectTransform.anchorMin = new Vector2(0, 0); quantity.rectTransform.anchorMax = new Vector2(1, 0);
            quantity.rectTransform.offsetMin = new Vector2(4, 3); quantity.rectTransform.offsetMax = new Vector2(-4, 26);
            quantityLabels[id] = quantity;
            Action update = () => quantity.text = TargetLabel(id);
            update();
            if (draft)
            {
                var sliderRoot = UiKit.Rect(r, "ShareSlider_" + id);
                sliderRoot.anchorMin = new Vector2(0, 0); sliderRoot.anchorMax = new Vector2(1, 0);
                sliderRoot.offsetMin = new Vector2(12, 27); sliderRoot.offsetMax = new Vector2(-12, 45);
                var track = UiKit.Panel(sliderRoot, "Track", Palette.Disabled, raycast: true); UiKit.Stretch(track.rectTransform, 0, 6, 0, 6);
                var handle = UiKit.Panel(sliderRoot, "Handle", Palette.Player(controller.LocalPlayerId), rounded: true, raycast: true);
                handle.rectTransform.sizeDelta = new Vector2(16, 18);
                var slider = sliderRoot.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = cap; slider.wholeNumbers = true;
                sliders[id] = slider;
                slider.handleRect = handle.rectTransform; slider.targetGraphic = handle; slider.SetValueWithoutNotify(targets[id]); slider.interactable = Editable;
                slider.onValueChanged.AddListener(value => { targets[id] = Mathf.RoundToInt(value); SelectCompany(id); update(); UpdatePreview(); });
            }
        }
        private void ShareBlock(RectTransform r, string id, int slot, Color color)
        {
            var block = UiKit.Panel(r, "ShareBlock_" + id + "_" + slot, color).rectTransform;
            block.anchorMin = new Vector2(slot / 20f, 1); block.anchorMax = new Vector2((slot + 1) / 20f, 1);
            block.offsetMin = new Vector2(3, -49); block.offsetMax = new Vector2(-1, -30);
        }
        private void SelectCompany(string id)
        {
            if (chosen.Contains(id)) return;
            int max = Portfolio.augments.Contains("FLEXIBLE") ? 2 : 1;
            if (chosen.Count >= max)
            {
                string removed = chosen[0]; chosen.RemoveAt(0); targets[removed] = Holding(removed);
                if (sliders.TryGetValue(removed, out var slider)) slider.SetValueWithoutNotify(targets[removed]);
                if (quantityLabels.TryGetValue(removed, out var text)) text.text = TargetLabel(removed);
            }
            chosen.Add(id);
            foreach (var pair in cardBackgrounds) pair.Value.color = chosen.Contains(pair.Key) ? Palette.AccentSoft : Palette.Card;
        }
        private string TargetLabel(string id) => targets[id] + "주 · 전체 발행량 " + (targets[id] * 5) + "%\n개인 한도 "
            + (targets[id] * 100f / Portfolio.LimitFor(id)).ToString("0.#") + "% · 내 자산 " + (targets[id] * 100f / 30).ToString("0.##") + "%";
        private void UpdatePreview()
        {
            if (apply == null || preview == null) return;
            int total = UiRules.CompanyIds.Sum(Holding) + chosen.Sum(id => targets[id] - Holding(id));
            bool changed = chosen.Any(id => targets[id] != Holding(id));
            preview.text = chosen.Count == 0 ? "기업 카드 또는 슬라이더로 목표량 선택 · 매도한 물량은 시장으로 돌아갑니다."
                : string.Join(" / ", chosen.Select(id => id + " " + Holding(id) + "→" + targets[id] + "주")) + " · 적용 후 " + total + "/30칸 · 현금 " + (30 - total) + "칸";
            preview.color = total > 30 ? Palette.Up : Palette.Text;
            apply.interactable = Editable && changed && total <= 30;
        }
        private void BuildPortfolio()
        {
            int slot = 0;
            foreach (var v in Portfolio.holdings ?? new PortfolioHolding[0])
                for (int n = 0; n < v.shares; n++) PortfolioBlock(slot++, CompanyColor(v.companyId));
            while (slot < 30) PortfolioBlock(slot++, Palette.Disabled);
        }
        private void PortfolioBlock(int slot, Color color)
        {
            var b = UiKit.Panel(root, "PortfolioSlot_" + slot, color).rectTransform;
            b.anchorMin = new Vector2(slot / 30f, 0); b.anchorMax = new Vector2((slot + 1) / 30f, 0);
            b.offsetMin = new Vector2(3, 72); b.offsetMax = new Vector2(-1, 90);
        }
        private static Color CompanyColor(string id)
        {
            Color32[] colors = { new Color32(64, 108, 245, 255), new Color32(139, 76, 238, 255), new Color32(33, 158, 99, 255), new Color32(211, 151, 36, 255), new Color32(230, 85, 114, 255), new Color32(39, 159, 188, 255) };
            return colors[Math.Max(0, Array.IndexOf(UiRules.CompanyIds, id))];
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
                string id = UiRules.CompanyIds[i]; var b = UiKit.Button(root, id, 12, focus == id ? Palette.AccentSoft : Palette.Card, Palette.Accent,
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
