using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BlackWhale.Bots;
using BlackWhale.Shared;
using BlackWhale.UI.Dev;
using UnityEditor;
using UnityEngine;

namespace BlackWhale.UI.EditorTools
{
    public sealed class BotLabWindow : EditorWindow
    {
        [SerializeField] private BotExperimentSettings settings = new BotExperimentSettings();
        private BotExperimentBatch batch;
        private Vector2 scroll;
        private int winMetric, selectedRun;
        private string error;
        private bool details = true;
        private static readonly string[] Personalities = { "모험", "안정", "전략" };
        private static readonly string[] Difficulties = { "쉬움", "보통", "어려움" };
        [MenuItem("BlackWhale/Bot 실험실", false, 121)]
        public static void Open()
        {
            var window = GetWindow<BotLabWindow>("Bot 실험실");
            window.minSize = new Vector2(800, 640); window.Show();
        }
        private void OnEnable() { EditorApplication.update += Tick; AssemblyReloadEvents.beforeAssemblyReload += Stop; }
        private void OnDisable() { EditorApplication.update -= Tick; AssemblyReloadEvents.beforeAssemblyReload -= Stop; Stop(); }
        private void Stop() { batch?.Dispose(); Repaint(); }
        private void Tick()
        {
            if (batch?.Running != true) return;
            var watch = Stopwatch.StartNew();
            try { do { batch.Step(); } while (batch.Running && watch.ElapsedMilliseconds < 8); }
            catch (Exception ex) { error = ex.ToString(); batch.Dispose(); }
            Repaint();
        }
        private void StartExperiment()
        {
            try {
                settings.Validate(); batch?.Dispose();
                batch = new BotExperimentBatch(settings, StockPatternLibraryResource.Load());
                selectedRun = 0; error = null;
            }
            catch (Exception ex) { error = ex.Message; }
        }
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Bot 실험실", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("실제 DevBot + 6턴 주식 게임 규칙 · 가상 시간으로 빠르게 실행 · 씬/네트워크 연결 불필요", EditorStyles.wordWrappedMiniLabel);
            bool running = batch?.Running == true;
            using (new EditorGUI.DisabledScope(running)) {
                for (int i = 0; i < 4; i++) {
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox)) {
                        EditorGUILayout.LabelField("BOT " + (i + 1), EditorStyles.boldLabel, GUILayout.Width(72));
                        settings.bots[i].personality = (BotPersonality)EditorGUILayout.Popup("성향", (int)settings.bots[i].personality, Personalities);
                        settings.bots[i].difficulty = (BotDifficulty)EditorGUILayout.Popup("난이도", (int)settings.bots[i].difficulty, Difficulties);
                    }
                }
                settings.repetitions = EditorGUILayout.IntField("시뮬레이션 횟수 N", settings.repetitions);
                settings.firstSeed = EditorGUILayout.IntField("시작 시드", settings.firstSeed);
                settings.rotateSeats = EditorGUILayout.Toggle("매 회차 BOT 좌석 순환", settings.rotateSeats);
                EditorGUILayout.LabelField("회차마다 시드 +1 · 좌석 순환과 무관하게 BOT 1~4의 설정/통계는 유지됩니다.", EditorStyles.wordWrappedMiniLabel);
            }
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(running)) if (GUILayout.Button("시뮬레이션 시작", GUILayout.Height(28))) StartExperiment();
                using (new EditorGUI.DisabledScope(!running)) if (GUILayout.Button("중지", GUILayout.Height(28))) Stop();
                using (new EditorGUI.DisabledScope(batch == null || batch.Completed == 0)) if (GUILayout.Button("결과 JSON 저장", GUILayout.Height(28))) Export();
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (batch != null) {
                var result = batch.Results;
                Rect bar = GUILayoutUtility.GetRect(1, 22, GUILayout.ExpandWidth(true));
                EditorGUI.ProgressBar(bar, batch.Completed / (float)result.settings.repetitions,
                    batch.Completed + " / " + result.settings.repetitions + (running ? " · " + batch.CurrentTurn + "턴 " + batch.CurrentPhase : " · " + (batch.Completed == result.settings.repetitions ? "완료" : "중지")));
                EditorGUILayout.LabelField("정보 공유 120초 · 구간 선택 10초 / 공개 2초 · 봇 기본 대기 " + result.thinkSeconds + "초", EditorStyles.miniLabel);
                EditorGUILayout.HelpBox("턴 우승 = 해당 턴 수익률 최고 / 누적 자산 1위 = 턴 종료 자산 최고. 동률은 공동 1위로 각각 집계하므로 합계가 N을 넘을 수 있습니다. 최종 우승은 실제 게임의 동점 규칙을 따릅니다. 중지 시 완료된 회차만 집계합니다.", MessageType.Info);
                if (batch.Completed > 0) { DrawWins(); DrawDetails(); }
            }
            EditorGUILayout.EndScrollView();
        }
        private string BotLabel(int bot) => "BOT " + (bot + 1);
        private string ProfileLabel(int bot)
        {
            var profile = batch.Results.settings.bots[bot];
            return BotLabel(bot) + " · " + Personalities[(int)profile.personality] + " / " + Difficulties[(int)profile.difficulty];
        }
        private void DrawWins()
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField("턴별 우승 횟수", EditorStyles.boldLabel);
            winMetric = EditorGUILayout.Popup("집계 기준", winMetric, new[] { "해당 턴 수익률 1위", "턴 종료 누적 자산 1위" });
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                GUILayout.Label("BOT · 성향 / 난이도", GUILayout.Width(205));
                for (int turn = 1; turn <= StockRules.Turns; turn++) GUILayout.Label(turn + "턴", GUILayout.MinWidth(50));
                GUILayout.Label("최종 우승", GUILayout.MinWidth(100));
            }
            for (int bot = 0; bot < 4; bot++) using (new EditorGUILayout.HorizontalScope()) {
                GUILayout.Label(ProfileLabel(bot), GUILayout.Width(205));
                for (int turn = 0; turn < StockRules.Turns; turn++) GUILayout.Label(
                    (winMetric == 0 ? batch.Summary.returnWins[bot, turn] : batch.Summary.assetLeads[bot, turn]).ToString(), GUILayout.MinWidth(50));
                int wins = batch.Summary.finalWins[bot];
                GUILayout.Label(wins + "회 (" + (100.0 * wins / batch.Completed).ToString("F1") + "%)", GUILayout.MinWidth(100));
            }
        }
        private void DrawAugments()
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField("선택 증강 · 턴별 선택 횟수", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) {
                GUILayout.Label("선택 턴 / 증강", GUILayout.Width(205));
                for (int bot = 0; bot < 4; bot++) GUILayout.Label(BotLabel(bot), GUILayout.MinWidth(90));
            }
            foreach (int turn in new[] { 1, 4 }) foreach (string augment in StockRules.Augments) {
                batch.Summary.augmentCounts.TryGetValue(turn + ":" + augment, out var counts);
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.Label(turn + "턴 / " + StockRules.Name(augment), GUILayout.Width(205));
                    for (int bot = 0; bot < 4; bot++) GUILayout.Label((counts?[bot] ?? 0).ToString(), GUILayout.MinWidth(90));
                }
            }
        }
        private void DrawDetails()
        {
            EditorGUILayout.Space(); details = EditorGUILayout.Foldout(details, "회차별 원본 결과", true);
            if (!details) return;
            selectedRun = Mathf.Clamp(EditorGUILayout.IntSlider("조회 회차", selectedRun + 1, 1, batch.Completed) - 1, 0, batch.Completed - 1);
            var run = batch.Results.runs[selectedRun];
            EditorGUILayout.LabelField("시드 " + run.seed + " · 최종 우승 " + BotLabel(run.finalWinner));
            EditorGUILayout.LabelField("좌석 P1~P4: " + string.Join(" / ", run.seatBots.Select(BotLabel)));
            EditorGUILayout.LabelField("서버 거절 명령: " + run.commandFailures + "회 (물량 경쟁 등 포함)", EditorStyles.miniLabel);
            foreach (var turn in run.turns) {
                EditorGUILayout.LabelField(turn.turn + "턴 수익률 우승: " + string.Join(", ", turn.returnWinners.Select(BotLabel)) +
                    " · 자산 1위: " + string.Join(", ", turn.assetLeaders.Select(BotLabel)), EditorStyles.boldLabel);
                for (int bot = 0; bot < 4; bot++) EditorGUILayout.LabelField(BotLabel(bot),
                    "수익률 " + (turn.returnsBps[bot] / 100.0).ToString("F2") + "% · 자산 " + turn.assets[bot].ToString("N0") +
                    (string.IsNullOrEmpty(turn.selectedAugments[bot]) ? "" : " · 선택 증강: " + string.Join(", ", turn.selectedAugments[bot].Split(',').Select(StockRules.Name))));
            }
        }
        private void Export()
        {
            string path = EditorUtility.SaveFilePanel("Bot 실험 결과 저장", "", "BotExperiment-" + batch.Completed + ".json", "json");
            if (string.IsNullOrEmpty(path)) return;
            try { File.WriteAllText(path, JsonUtility.ToJson(batch.Results, true)); }
            catch (Exception ex) { error = ex.Message; }
        }
    }
}
