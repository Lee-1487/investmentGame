// Live lobby uses per-bot profiles; Show is retained for the standalone gallery.
using System;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    public sealed class BotDifficultyPanel
    {
        private readonly RectTransform _root;

        public Action<BotDifficulty> Changed;

        public Action<string, string, string> ProfileChanged;
        public void ShowProfiles(Player[] players, bool isHost, bool inLobby = true)
        {
            UiKit.Clear(_root);
            ScreenKit.Title(_root, "<b>BOT 성향 · 난이도</b>  <size=14>대기실에서 방장만 개별 변경</size>");
            var bots = (players ?? Array.Empty<Player>()).Where(p => p.isBot).ToArray();
            for (int i = 0; i < bots.Length; i++) {
                var bot = bots[i];
                var row = UiKit.Rect(_root, "BotProfile_" + bot.playerId);
                row.anchorMin = new Vector2(0, 1); row.anchorMax = new Vector2(1, 1);
                row.offsetMin = new Vector2(8, -180 - i * 132); row.offsetMax = new Vector2(-8, -60 - i * 132);
                var label = UiKit.Label(row, bot.displayName, 18, Palette.Text);
                UiKit.Place(label.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(85, -16), new Vector2(170, 30));
                string[] styles = { "Adventurous", "Stable", "Strategic" }, names = { "모험", "안정", "전략" };
                string[] levels = { "Easy", "Normal", "Hard" }, levelNames = { "쉬움", "보통", "어려움" };
                for (int j = 0; j < 3; j++) {
                    string style = styles[j], level = levels[j];
                    var styleButton = UiKit.Button(row, names[j], 14, bot.botPersonality == style ? Palette.AccentSoft : Palette.Card, Palette.Text,
                        () => ProfileChanged?.Invoke(bot.playerId, bot.botDifficulty, style));
                    styleButton.name = "BotPersonality_" + bot.playerId + "_" + style;
                    UiKit.Place((RectTransform)styleButton.transform, new Vector2(.24f + j * .28f, 1), new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(140, 32));
                    styleButton.interactable = isHost && inLobby;
                    var levelButton = UiKit.Button(row, levelNames[j], 14, bot.botDifficulty == level ? Palette.AccentSoft : Palette.Card, Palette.Text,
                        () => ProfileChanged?.Invoke(bot.playerId, level, bot.botPersonality));
                    levelButton.name = "BotDifficulty_" + bot.playerId + "_" + level;
                    UiKit.Place((RectTransform)levelButton.transform, new Vector2(.24f + j * .28f, 1), new Vector2(.5f, 1), new Vector2(0, -76), new Vector2(140, 32));
                    levelButton.interactable = isHost && inLobby;
                }
            }
            string note = bots.Length == 0 ? "대기실에서 BOT을 추가하면 각 BOT의 성향과 난이도를 선택할 수 있습니다."
                : "모험: AI·BIO·SPACE / 안정: FOOD·AUTO / 전략: 전체 기업의 기대 수익 비교\n쉬움 → 어려움: 예측·시점·경쟁 판단 오차와 반응 지연 감소. 정보 코인은 동일합니다.";
            var foot = UiKit.Label(_root, note, 13, Palette.SubText);
            UiKit.Place(foot.rectTransform, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 38), new Vector2(650, 65));
        }

        public BotDifficultyPanel(RectTransform root) => _root = root;

        public void Show(BotDifficulty selected, bool isHost, bool inLobby = true)
        {
            UiKit.Clear(_root);
            bool editable = isHost && inLobby;

            ScreenKit.Title(_root, "<b>BOT 난이도</b>   <size=14><color=#B39C74>새 BOT의 기본값 · 대기실에서 방장만 변경</color></size>");

            var row = UiKit.Rect(_root, "Options");
            UiKit.Stretch(row, 0, 48, 0, 70);
            var values = new[] { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard };
            for (int i = 0; i < values.Length; i++)
            {
                var d = values[i];
                bool on = d == selected;
                var cell = UiKit.Rect(row, "Difficulty_" + d);
                UiKit.Anchor(cell, i / 3f, 0, (i + 1) / 3f, 1, 8, 0);
                var frame = ScreenKit.Card(cell, "Frame", on ? Palette.Accent : Palette.CardBorder, on ? Palette.AccentSoft : Palette.Card, out var inner, on ? 3f : 1f);
                UiKit.Stretch(frame);

                var hit = UiKit.Button(inner, "", 12, Color.clear, Palette.Text, () => Changed?.Invoke(d));
                UiKit.Stretch((RectTransform)hit.transform);
                hit.name = "BotDifficulty_" + d;
                hit.interactable = editable;
                var colors = hit.colors;
                colors.disabledColor = Color.clear;
                hit.colors = colors;

                // 난이도 막대(1~3칸)
                for (int k = 0; k < 3; k++)
                {
                    var pip = UiKit.Panel(inner, "Pip" + k, k <= i ? (on ? Palette.Accent : Palette.SubText) : Palette.Disabled, rounded: true);
                    UiKit.Place(pip.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2((k - 1) * 26, -28), new Vector2(20, 20));
                }

                var text = UiKit.Label(inner, "<size=24><b>" + BotDifficultyInfo.Name(d) + "</b></size>" + (on ? "\n<size=12><color=#A08A66><b>선택됨</b></color></size>" : "\n ")
                        + "\n\n" + BotDifficultyInfo.Describe(d),
                    14, Palette.Text, TextAnchor.UpperCenter);
                UiKit.Stretch(text.rectTransform, 18, 62, 18, 12);
            }

            string note = !inLobby ? "게임이 시작되면 난이도를 바꿀 수 없습니다."
                : !isHost ? "방장만 바꿀 수 있습니다. 현재 설정: " + BotDifficultyInfo.Name(selected)
                : "새로 추가할 BOT의 기본 난이도입니다. 기존 BOT은 개별 설정을 유지합니다. 어떤 난이도도 숨은 시세를 보지 않고, 자기에게 공개된 정보만 씁니다.";
            var foot = UiKit.Label(_root, note, 13, Palette.SubText, TextAnchor.MiddleLeft);
            UiKit.Place(foot.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 20), new Vector2(1000, 40));
        }
    }
}
