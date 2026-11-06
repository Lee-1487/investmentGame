using System;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.State;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    // Minimal integration controls for A's new phases; D can restyle without changing commands.
    public sealed class ProgressionPanel : MonoBehaviour
    {
        private GameClientController controller;
        private RectTransform card;
        private string key;
        private Text status;
        private Button confirm;
        private InputField amount, preferences;
        private Toggle freePass;
        public static void Create(RectTransform parent, GameClientController controller)
        {
            var root = UiKit.Rect(parent, "ProgressionPanel"); UiKit.Stretch(root);
            var panel = root.gameObject.AddComponent<ProgressionPanel>(); panel.controller = controller;
            panel.card = UiKit.Panel(root, "ProgressionCard", Palette.Card, rounded: true, raycast: true).rectTransform;
            UiKit.Place(panel.card, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(620, 350));
            controller.Store.Changed += panel.Refresh; panel.Refresh();
        }
        private void OnDestroy() { if (controller != null) controller.Store.Changed -= Refresh; }
        private Text Label(string text, float y, int size = 16)
        {
            var label = UiKit.Label(card, text, size, Palette.Text);
            UiKit.Place(label.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, y), new Vector2(580, 44));
            return label;
        }
        private Button Button(string text, float y, Action action)
        {
            var button = UiKit.Button(card, text, 16, Palette.Accent, Color.white, action);
            UiKit.Place((RectTransform)button.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, y), new Vector2(540, 40));
            return button;
        }
        private void Refresh()
        {
            var state = controller.Store.Public; var view = controller.Store.View;
            bool active = state != null && (state.phase == Protocol.AbilitySelection || state.phase == Protocol.Auction);
            card.gameObject.SetActive(active);
            if (!active) { key = null; return; }
            if (view == null || view.turn != state.turn) return;
            string next = state.turn + "|" + state.phase;
            if (next != key)
            {
                key = next; UiKit.Clear(card);
                if (state.phase == Protocol.AbilitySelection)
                {
                    Label("고유 능력 선택", -15, 22);
                    int index = 0;
                    foreach (string id in view.ability?.candidates ?? Array.Empty<string>())
                    {
                        string selected = id; var button = Button(AbilityName(id), -80 - index++ * 52, () => controller.SelectAbility(selected));
                        button.name = "ChooseAbility_" + id;
                    }
                    status = Label("", -240, 14);
                    if (controller.IsHost)
                    {
                        confirm = Button("미선택자는 첫 후보로 진행", -296, () => controller.FinishPhase());
                        confirm.name = "DefaultAbilities";
                    }
                }
                else
                {
                    Label(state.turn + "턴 · 비밀 입찰", -15, 22);
                    Label("상한 " + view.auction.maximumBid + " · 미제출 0 · 확정 후 변경 불가", -58, 14);
                    amount = UiKit.NumberInput(card, "입찰액", 18); amount.text = "0"; amount.name = "BidAmount";
                    UiKit.Place((RectTransform)amount.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -110), new Vector2(540, 38));
                    if (state.turn == 9)
                    {
                        preferences = UiKit.NumberInput(card, "자리 선호 순서", 16);
                        preferences.contentType = InputField.ContentType.Standard; preferences.text = "A,B,C,D";
                        UiKit.Place((RectTransform)preferences.transform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -160), new Vector2(540, 38));
                        var toggleRoot = UiKit.Rect(card, "UseFreePass");
                        UiKit.Place(toggleRoot, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -210), new Vector2(540, 30));
                        freePass = toggleRoot.gameObject.AddComponent<Toggle>();
                        var image = UiKit.Panel(toggleRoot, "Check", Palette.Accent); UiKit.Place(image.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(15, 0), new Vector2(20, 20));
                        freePass.graphic = image; freePass.targetGraphic = image;
                        var caption = UiKit.Label(toggleRoot, "프리패스 사용 (보유 시)", 14, Palette.Text); UiKit.Stretch(caption.rectTransform, 40, 0, 0, 0);
                        freePass.interactable = view.inventory?.Contains("FREE_PASS") == true;
                    }
                    confirm = Button("입찰 확정", -263, SubmitBid); confirm.name = "ConfirmBid";
                    status = Label("", -307, 13);
                }
            }
            if (state.phase == Protocol.AbilitySelection)
            {
                bool locked = !string.IsNullOrEmpty(view.ability?.selectedId) || controller.Store.HasPending(CommandTypes.SelectAbility);
                foreach (var button in card.GetComponentsInChildren<Button>())
                    if (button.name.StartsWith("ChooseAbility_")) button.interactable = !locked;
                status.text = string.IsNullOrEmpty(view.ability?.selectedId) ? "후보 중 하나를 선택하세요." : AbilityName(view.ability.selectedId) + " 선택 완료 · 다른 참가자 대기";
            }
            else
            {
                bool locked = view.auction.confirmed || controller.Store.HasPending(CommandTypes.SubmitBid);
                confirm.interactable = !locked; amount.interactable = !locked;
                if (preferences != null) preferences.interactable = !locked;
                if (freePass != null) freePass.interactable = !locked && view.inventory?.Contains("FREE_PASS") == true;
                status.text = view.auction.confirmed ? "입찰 " + view.auction.bidAmount + " 확정 · 다른 참가자 대기" : "타인의 입찰액은 공개되지 않습니다.";
            }
        }
        private void SubmitBid()
        {
            if (!long.TryParse(amount.text, out long bid) || bid < 0 || bid > controller.Store.View.auction.maximumBid)
            { status.text = "입찰액은 0부터 상한까지 정수로 입력하세요."; return; }
            controller.SubmitBid(bid, preferences == null ? null : preferences.text.ToUpperInvariant().Split(',').Select(s => s.Trim()).ToArray(), freePass != null && freePass.isOn);
        }
        public static string AbilityName(string id)
        {
            switch (id)
            {
                case "POLITICIAN": return "정치인";
                case "SYNDICATE": return "작전 세력";
                case "INFORMATION_BROKER": return "정보상";
                case "CORPORATE_RAIDER": return "기업사냥꾼";
                case "GAMBLER": return "도박사";
                default: return id ?? "";
            }
        }
    }
}
