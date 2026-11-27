using System;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed partial class TabletView
    {
        private bool _socialPinned;
        private int _socialTurn;
        private string _socialTarget;
        private RectTransform _socialSlot;
        private readonly System.Collections.Generic.HashSet<string> _seenSocial = new System.Collections.Generic.HashSet<string>();

        private void OnSocialCommandSucceeded(CommandResult result)
        {
            if (result.type == StockRules.ShareInformation + "_RESULT") ShowBanner("정보 교환 요청을 보냈습니다.");
            if (result.type == StockRules.RespondExchange + "_RESULT") ShowBanner("정보 교환 요청에 응답했습니다.");
            if (result.type == StockRules.SendPlan + "_RESULT") ShowBanner("진입·정산 계획을 전달했습니다.");
        }

        private void RefreshSocialInbox()
        {
            if (Store.Public?.stockSystem != true || Store.Public.phase != Protocol.Info || !_c.HasCurrentPlayerView) return;
            foreach (var company in Store.View.companies ?? Array.Empty<VisibleCompany>())
                foreach (var card in company.visibleWindows ?? Array.Empty<VisibleInformationWindow>())
                    if (card.source == "SHARED" && _seenSocial.Add("card:" + card.recordId))
                        ShowBanner(company.companyId + " " + card.startDay + "~" + (card.startDay + 4) + "일 카드를 전달받았습니다.");
            foreach (var ex in Store.View.informationExchanges ?? Array.Empty<InformationExchange>())
            {
                if (ex.senderId != _c.LocalPlayerId && ex.targetPlayerId != _c.LocalPlayerId) continue;
                if (ex.status == "PENDING" && ex.targetPlayerId == _c.LocalPlayerId && _seenSocial.Add("ex-in:" + ex.id))
                    ShowBanner(ex.senderId + "의 교환 요청 · " + ex.offeredCompanyId + " " + ex.offeredStartDay + "~" + (ex.offeredStartDay + 4)
                        + "일 ↔ " + ex.requestedStartDay + "~" + (ex.requestedStartDay + 4) + "일");
                else if ((ex.status == "ACCEPTED" || ex.status == "REJECTED" || ex.status == "CANCELLED")
                    && _seenSocial.Add("ex-done:" + ex.id + ":" + ex.status))
                {
                    string statusText = ex.status == "ACCEPTED" ? "성사되었습니다." : ex.status == "REJECTED" ? "거절되었습니다." : "취소되었습니다.";
                    ShowBanner(ex.offeredCompanyId + " " + ex.offeredStartDay + "~" + (ex.offeredStartDay + 4) + "일 ↔ "
                        + ex.requestedStartDay + "~" + (ex.requestedStartDay + 4) + "일 정보 교환이 " + statusText);
                }
            }
            foreach (var intent in Store.View.receivedTradeIntents ?? Array.Empty<TradeIntent>())
                if (_seenSocial.Add(Store.Public.turn + ":" + intent.senderId + ":" + intent.companyId + ":" + intent.entryDay + ":" + intent.exitDay))
                    ShowBanner(intent.senderId + "의 계획 · " + intent.companyId + " " + intent.entryDay + "일 진입 → " + intent.exitDay + "일 정산");
        }

        private void CloseSocialPopup()
        {
            _socialPinned = false; _dim.raycastTarget = false; HidePopup();
        }

        private Text SocialLabel(RectTransform parent, string value, float top, float height, int size = 13)
        {
            var label = UiKit.Label(parent, value, size, Palette.Text, TextAnchor.UpperLeft);
            label.rectTransform.anchorMin = new Vector2(0, 1); label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(16, -top - height); label.rectTransform.offsetMax = new Vector2(-16, -top);
            return label;
        }

        private Button SocialButton(RectTransform parent, string name, string text, float top, Action click)
        {
            var button = UiKit.Button(parent, text, 13, Palette.AccentSoft, Palette.Text, click);
            button.name = name;
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(16, -top - 34); rect.offsetMax = new Vector2(-16, -top);
            button.interactable = !Store.Public.paused;
            return button;
        }

        private RectTransform SocialFrame(string title, float height)
        {
            var panel = OpenPopup(new Vector2(380, height));
            _dim.raycastTarget = true;
            PositionNextTo(panel, _socialSlot, true);
            SocialLabel(panel, title, 14, 30, 17);
            var close = UiKit.Button(panel, "닫기", 11, Palette.Card, Palette.SubText, CloseSocialPopup);
            close.name = "CloseSocialPopup";
            UiKit.Place((RectTransform)close.transform, Vector2.one, Vector2.one, new Vector2(-10, -10), new Vector2(48, 26));
            return panel;
        }

        private void ShowSocialPopup(string target, RectTransform slot)
        {
            _socialPinned = true; _socialTurn = Store.Public.turn; _socialTarget = target; _socialSlot = slot;
            var exchanges = (Store.View?.informationExchanges ?? Array.Empty<InformationExchange>())
                .Where(e => e.status == "PENDING" && (e.senderId == target || e.targetPlayerId == target)).ToArray();
            var incoming = exchanges.FirstOrDefault(e => e.senderId == target && e.targetPlayerId == _c.LocalPlayerId);
            var outgoing = exchanges.FirstOrDefault(e => e.senderId == _c.LocalPlayerId && e.targetPlayerId == target);
            float extraHeight = incoming != null ? 104 : outgoing != null ? 54 : 0;
            var panel = SocialFrame(target + "에게 보내기", 330 + extraHeight);
            SocialLabel(panel, "합의 정보 교환이나 진입 계획을 선택하세요.", 48, 28);
            float y = 180;
            if (incoming != null)
            {
                SocialLabel(panel, "받은 교환 요청: 상대 " + incoming.offeredCompanyId + " " + incoming.offeredStartDay + "~" + (incoming.offeredStartDay + 4)
                    + "일 ↔ 내 " + incoming.requestedCompanyId + " " + incoming.requestedStartDay + "~" + (incoming.requestedStartDay + 4) + "일", y, 42, 12);
                var accept = UiKit.Button(panel, "수락", 13, Palette.Accent, Palette.OnAccent, () => {
                    if (_c.RespondInformationExchange(incoming.id, true) == CommandStatus.Sent) CloseSocialPopup();
                });
                accept.name = "AcceptExchange_" + incoming.id;
                accept.interactable = !Store.Public.paused;
                var ar = (RectTransform)accept.transform;
                ar.anchorMin = new Vector2(0, 1); ar.anchorMax = new Vector2(0.5f, 1);
                ar.offsetMin = new Vector2(16, -(y + 44) - 34); ar.offsetMax = new Vector2(-4, -(y + 44));
                var reject = UiKit.Button(panel, "거절", 13, Palette.Card, Palette.Text, () => {
                    if (_c.RespondInformationExchange(incoming.id, false) == CommandStatus.Sent) CloseSocialPopup();
                });
                reject.name = "RejectExchange_" + incoming.id;
                reject.interactable = !Store.Public.paused;
                var rr = (RectTransform)reject.transform;
                rr.anchorMin = new Vector2(0.5f, 1); rr.anchorMax = Vector2.one;
                rr.offsetMin = new Vector2(4, -(y + 44) - 34); rr.offsetMax = new Vector2(-16, -(y + 44));
                y += 96;
            }
            else if (outgoing != null)
            {
                SocialLabel(panel, "보낸 교환 요청 대기 중: 내 " + outgoing.offeredCompanyId + " " + outgoing.offeredStartDay + "~" + (outgoing.offeredStartDay + 4)
                    + "일 ↔ 상대 " + outgoing.requestedCompanyId + " " + outgoing.requestedStartDay + "~" + (outgoing.requestedStartDay + 4) + "일", y, 44, 12);
                y += 52;
            }
            var received = (Store.View?.receivedTradeIntents ?? Array.Empty<TradeIntent>()).Where(i => i.senderId == target).ToArray();
            SocialLabel(panel, "받은 계획 (실제 주문과 다를 수 있음)\n" + (received.Length == 0 ? "아직 받은 계획이 없습니다."
                : string.Join("\n", received.Select(i => i.companyId + " · " + i.entryDay + "일 진입 → " + i.exitDay + "일 정산"))), y, 132, 12);
        }

        private void ShowShareCards()
        {
            var known = (Store.View?.companies ?? Array.Empty<VisibleCompany>())
                .Where(c => c.companyId == "FOOD" || c.companyId == "AUTO")
                .SelectMany(c => (c.visibleWindows ?? Array.Empty<VisibleInformationWindow>())
                    .Where(w => w.source == "DEALT" && w.startDay >= 6 && w.startDay <= 26 && w.dailyChangesBps?.Length == 5)
                    .Select(w => new { Company = c.companyId, Start = w.startDay }))
                .GroupBy(c => c.Company + ":" + c.Start).Select(g => g.First())
                .OrderBy(c => c.Company).ThenBy(c => c.Start).ToArray();
            var knownSet = new System.Collections.Generic.HashSet<string>((Store.View?.companies ?? Array.Empty<VisibleCompany>())
                .SelectMany(c => (c.visibleWindows ?? Array.Empty<VisibleInformationWindow>())
                    .Where(w => w.dailyChangesBps?.Length == 5)
                    .Select(w => c.companyId + ":" + w.startDay)));
            var wanted = new[] { "FOOD", "AUTO" }
                .SelectMany(comp => new[] { 6, 11, 16, 21, 26 }.Select(start => new { Company = comp, Start = start }))
                .Where(c => !knownSet.Contains(c.Company + ":" + c.Start))
                .ToArray();
            var panel = SocialFrame(_socialTarget + " · 정보 교환 요청", 540);
            SocialLabel(panel, "제공할 내 카드와 요청할 상대 카드를 고르세요.\n상대가 수락하면 두 카드가 동시에 교환됩니다.", 48, 44);
            if (known.Length == 0 || wanted.Length == 0)
            {
                SocialLabel(panel, known.Length == 0 ? "교환할 수 있는 개인 카드가 없습니다." : "요청할 미보유 구간 카드가 없습니다.", 108, 40);
                SocialButton(panel, "BackToSocialMenu", "← 메뉴", 494, () => ShowSocialPopup(_socialTarget, _socialSlot));
                return;
            }
            int selOffered = 0, selWanted = 0;
            SocialLabel(panel, "1. 내가 줄 카드", 96, 22, 12);
            var offerVp = UiKit.Rect(panel, "OfferCardList"); UiKit.Stretch(offerVp, 0, 120, 0, 274);
            var offerContent = UiKit.ScrollContent(offerVp, "OfferCards");
            offerContent.sizeDelta = new Vector2(0, Math.Max(42, known.Length * 42));
            var offerButtons = new Button[known.Length];

            SocialLabel(panel, "2. 받고 싶은 카드", 272, 22, 12);
            var wantVp = UiKit.Rect(panel, "WantCardList"); UiKit.Stretch(wantVp, 0, 296, 0, 108);
            var wantContent = UiKit.ScrollContent(wantVp, "WantCards");
            wantContent.sizeDelta = new Vector2(0, Math.Max(42, wanted.Length * 42));
            var wantButtons = new Button[wanted.Length];

            void RefreshExchangeSelection()
            {
                for (int i = 0; i < known.Length; i++)
                {
                    var card = known[i];
                    UiKit.SetButton(offerButtons[i], card.Company + " · " + card.Start + "~" + (card.Start + 4) + "일", !Store.Public.paused,
                        i == selOffered ? Palette.Accent : Palette.AccentSoft, i == selOffered ? Palette.OnAccent : Palette.Text);
                }
                for (int i = 0; i < wanted.Length; i++)
                {
                    var card = wanted[i];
                    UiKit.SetButton(wantButtons[i], card.Company + " · " + card.Start + "~" + (card.Start + 4) + "일", !Store.Public.paused,
                        i == selWanted ? Palette.Accent : Palette.AccentSoft, i == selWanted ? Palette.OnAccent : Palette.Text);
                }
            }
            for (int i = 0; i < known.Length; i++)
            {
                int index = i;
                offerButtons[i] = SocialButton(offerContent, "OfferCard_" + known[i].Company + "_" + known[i].Start, "", i * 42,
                    () => { selOffered = index; RefreshExchangeSelection(); });
            }
            for (int i = 0; i < wanted.Length; i++)
            {
                int index = i;
                wantButtons[i] = SocialButton(wantContent, "WantCard_" + wanted[i].Company + "_" + wanted[i].Start, "", i * 42,
                    () => { selWanted = index; RefreshExchangeSelection(); });
            }
            SocialButton(panel, "SendExchangeRequest", "정보 교환 요청 보내기", 446, () => {
                var o = known[selOffered];
                var w = wanted[selWanted];
                if (_c.ShareInformation(_socialTarget, o.Company, o.Start, w.Company, w.Start) == CommandStatus.Sent)
                    CloseSocialPopup();
            });
            SocialButton(panel, "BackToSocialMenu", "← 메뉴", 494, () => ShowSocialPopup(_socialTarget, _socialSlot));
            RefreshExchangeSelection();
        }

        private void ShowIntentEditor()
        {
            var ranges = Store.TradePlans.ToReservations().SelectMany(p => (p.intervals ?? Array.Empty<Interval>())
                .Select(i => new { Company = p.companyId, Interval = i })).ToArray();
            if (ranges.Length == 0) { ShowCustomIntentEditor(); return; }
            int selected = 0;
            var panel = SocialFrame(_socialTarget + " · 예약 구간 선택", 510);
            SocialLabel(panel, "예약한 구간 중 전달할 계획을 고르세요.\n선택한 내용은 상대에게만 전달됩니다.", 48, 48);
            var viewport = UiKit.Rect(panel, "IntentReservationList"); UiKit.Stretch(viewport, 0, 104, 0, 140);
            var content = UiKit.ScrollContent(viewport, "IntentReservations");
            content.sizeDelta = new Vector2(0, Math.Max(44, ranges.Length * 44));
            var buttons = new Button[ranges.Length];
            var preview = SocialLabel(panel, "", 382, 28, 13);
            void RefreshSelection() {
                for (int n = 0; n < ranges.Length; n++) {
                    var item = ranges[n];
                    UiKit.SetButton(buttons[n], item.Company + " · " + item.Interval.entryDay + "~" + item.Interval.exitDay + "일", !Store.Public.paused,
                        n == selected ? Palette.Accent : Palette.AccentSoft, n == selected ? Palette.OnAccent : Palette.Text);
                }
                var chosen = ranges[selected];
                preview.text = chosen.Company + " · " + chosen.Interval.entryDay + "일 진입 → " + chosen.Interval.exitDay + "일 정산";
            }
            for (int i = 0; i < ranges.Length; i++) {
                int index = i;
                buttons[i] = SocialButton(content, "SelectReservedIntent_" + i, "", i * 44, () => { selected = index; RefreshSelection(); });
            }
            SocialButton(panel, "SendReservedTradeIntent", "선택한 예약 구간 보내기", 418, () => {
                var chosen = ranges[selected];
                if (_c.SendTradeIntent(_socialTarget, chosen.Company, chosen.Interval.entryDay, chosen.Interval.exitDay) == CommandStatus.Sent) CloseSocialPopup();
            });
            SocialButton(panel, "ChooseCustomTradeIntent", "다른 구간 직접 선택", 464, ShowCustomIntentEditor);
            RefreshSelection();
        }

        private void ShowCustomIntentEditor()
        {
            string company = "FOOD"; int entry = 0, exit = 0;
            var panel = SocialFrame(_socialTarget + " · 계획 통보", 440);
            SocialLabel(panel, "상대의 동의 없이 전달되는 계획입니다.\n자동 주문이 되지 않으며 실제 선택과 달라도 됩니다.", 48, 48);
            var companyButtons = new Button[UiRules.CompanyIds.Length];
            var entryButtons = new Button[6]; var exitButtons = new Button[6];
            Text preview = null;
            void RefreshIntent() {
                for (int i = 0; i < companyButtons.Length; i++) UiKit.SetButton(companyButtons[i], UiRules.CompanyIds[i], !Store.Public.paused,
                    company == UiRules.CompanyIds[i] ? Palette.Accent : Palette.AccentSoft, company == UiRules.CompanyIds[i] ? Palette.OnAccent : Palette.Text);
                for (int i = 0; i < 6; i++) {
                    UiKit.SetButton(entryButtons[i], (i * 5 + 1) + "일", !Store.Public.paused,
                        entry == i ? Palette.Accent : Palette.AccentSoft, entry == i ? Palette.OnAccent : Palette.Text);
                    UiKit.SetButton(exitButtons[i], (i * 5 + 5) + "일", !Store.Public.paused && i >= entry,
                        exit == i ? Palette.Accent : Palette.AccentSoft, exit == i ? Palette.OnAccent : Palette.Text);
                }
                preview.text = company + " · " + (entry * 5 + 1) + "일 진입 → " + (exit * 5 + 5) + "일 정산";
            }
            void PlaceChoice(Button button, int index, int count, float top) {
                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(index / (float)count, 1); rect.anchorMax = new Vector2((index + 1) / (float)count, 1);
                rect.offsetMin = new Vector2(4, -top - 34); rect.offsetMax = new Vector2(-4, -top);
            }
            for (int i = 0; i < companyButtons.Length; i++) {
                int index = i;
                companyButtons[i] = UiKit.Button(panel, UiRules.CompanyIds[i], 12, Palette.Card, Palette.Text,
                    () => { company = UiRules.CompanyIds[index]; RefreshIntent(); });
                PlaceChoice(companyButtons[i], i, companyButtons.Length, 112);
            }
            SocialLabel(panel, "진입할 구간 시작일", 164, 24);
            for (int i = 0; i < 6; i++) {
                int index = i;
                entryButtons[i] = UiKit.Button(panel, "", 12, Palette.Card, Palette.Text,
                    () => { entry = index; exit = Math.Max(exit, entry); RefreshIntent(); });
                exitButtons[i] = UiKit.Button(panel, "", 12, Palette.Card, Palette.Text,
                    () => { exit = index; RefreshIntent(); });
                PlaceChoice(entryButtons[i], i, 6, 192); PlaceChoice(exitButtons[i], i, 6, 270);
            }
            SocialLabel(panel, "정산할 구간 마지막 날", 242, 24);
            preview = SocialLabel(panel, "", 324, 28, 14);
            SocialButton(panel, "SendTradeIntent", "이 계획 보내기", 364, () => {
                if (_c.SendTradeIntent(_socialTarget, company, entry * 5 + 1, exit * 5 + 5) == CommandStatus.Sent) {
                    CloseSocialPopup();
                }
            });
            RefreshIntent();
        }
    }
}
