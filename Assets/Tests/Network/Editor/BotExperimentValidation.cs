using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Bots;
using BlackWhale.Rules;
using BlackWhale.Shared;
using BlackWhale.UI.Dev;

namespace BlackWhale.Tests
{
    public static class BotExperimentValidation
    {
        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("BOT-LAB: " + message); }
        private static BotExperimentBatch Run(BotExperimentSettings settings)
        {
            var batch = new BotExperimentBatch(settings, StockPatternLibrary.CreateDefault());
            while (batch.Running) batch.Step();
            return batch;
        }
        public static string[] RunAll(ITestJson json)
        {
            var settings = new BotExperimentSettings { repetitions = 4, firstSeed = 123 };
            settings.bots[0].difficulty = BotDifficulty.Easy;
            settings.bots[2].difficulty = BotDifficulty.Hard;
            using (var first = Run(settings)) using (var second = Run(settings)) {
                Check(first.Completed == 4 && first.Results.runs.All(r => r.turns.Count == StockRules.Turns), "all runs settle all six turns");
                Check(json.Encode(first.Results) == json.Encode(second.Results), "identical settings and seed reproduce market, information, decisions and results");
                Check(first.Summary.finalWins.Sum() == 4, "one final winner per completed game");
                for (int bot = 0; bot < 4; bot++) {
                    Check(first.Results.runs.Select(r => Array.IndexOf(r.seatBots, bot)).Distinct().Count() == 4, "stable bot identity rotates through each seat");
                    foreach (int turn in new[] { 1, 4 }) Check(first.Summary.augmentCounts.Where(p => p.Key.StartsWith(turn + ":")).Sum(p => p.Value[bot]) == 4,
                        "each bot chooses one augment per augmentation round per game");
                }
                foreach (var run in first.Results.runs) foreach (var turn in run.turns) {
                    Check(turn.returnWinners.All(b => turn.returnsBps[b] == turn.returnsBps.Max()), "turn winners use current return, not cumulative assets");
                    Check(turn.assetLeaders.All(b => turn.assets[b] == turn.assets.Max()), "asset leaders use settled assets");
                    Check(turn.selectedAugments.All(a => turn.turn == 1 || turn.turn == 4 ? StockRules.Augments.Contains(a) : string.IsNullOrEmpty(a)),
                        "record new augment choices on the actual selection turn only");
                }
                Console.WriteLine("BOT-LAB sample: " + string.Join(" / ", first.Summary.finalWins.Select((w, i) => "BOT " + (i + 1) + "=" + w)));
            }
            using (var stopped = new BotExperimentBatch(settings, StockPatternLibrary.CreateDefault())) {
                stopped.Step(); stopped.Dispose(); stopped.Step();
                Check(stopped.Completed == 0 && !stopped.Running, "cancel discards partial game and releases simulation");
            }
            using (var snapshot = new BotExperimentBatch(settings, StockPatternLibrary.CreateDefault())) {
                settings.bots[0].difficulty = BotDifficulty.Hard;
                Check(snapshot.Results.settings.bots[0].difficulty == BotDifficulty.Easy, "configuration snapshot isolated from edits");
            }
            var ties = new BotExperimentSummary();
            ties.Add(new BotExperimentRun { finalWinner = 0, turns = new List<BotExperimentTurn> { new BotExperimentTurn {
                turn = 1, returnWinners = new[] { 0, 1 }, assetLeaders = new[] { 2, 3 } } } });
            Check(ties.returnWins[0, 0] == 1 && ties.returnWins[1, 0] == 1 && ties.assetLeads[2, 0] == 1 && ties.assetLeads[3, 0] == 1,
                "joint winners counted for each bot without mixing metrics");
            return new[] { "four-real-bots-six-turn-games-and-augmentation-records", "seed-reproducibility-and-seat-rotation", "win-metrics-joint-winners-and-final-game-winner", "cancellation-and-settings-snapshot-isolation" };
        }
    }
}
