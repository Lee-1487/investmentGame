using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.Rules
{
    public sealed partial class MarketService
    {
        public bool DecisionOpen { get; private set; }
        private int aiRiseStreak, bioExpectationBps;
        private int aiUpProbabilityBps = 5000, pendingAiUpProbabilityBps = 5000;
        private int pendingBioExpectationBps;
        private bool bioOccurred, pendingBioOccurred;
        private static readonly int[] AiUp = { 5, 8, 12, 16, 22, 30 };
        private static readonly int[] AiDown = { 8, 12, 18, 25, 35, 45 };

        public static int AccumulateAiUpChance(int previousBps, int investors, bool fell = false)
        {
            if (investors < 0 || investors > 4) throw new ArgumentOutOfRangeException(nameof(investors));
            int[] changes = { 500, -1000, 500, 1000, 1500 };
            int bonus = fell ? 1000 : 0;
            return Math.Max(500, Math.Min(9000, previousBps + changes[investors] + bonus));
        }

        public static int AiSegmentReturnBps(int streak, int tokens, bool rising)
        {
            if (tokens < 0 || tokens > 12) throw new ArgumentOutOfRangeException(nameof(tokens));
            if (tokens == 0) return 0;
            int index = Math.Max(0, Math.Min(5, streak));
            int magnitude = (rising ? AiUp[index] : AiDown[index]) * 100 * (10 + (tokens - 1) * 2) / 10;
            return rising ? magnitude : -Math.Min(9500, magnitude);
        }

        private readonly List<StockSegmentRecord> segmentHistory = new List<StockSegmentRecord>();
        private CompanyShareState[] publicShares;

        private static CompanyShareState[] CopyShares(CompanyShareState[] source) => source?.Select(c => new CompanyShareState {
            companyId = c.companyId, totalShares = c.totalShares, fills = c.fills?.ToArray(),
            owners = c.owners.Select(o => new ShareOwner { playerId = o.playerId, shares = o.shares, reserved = o.reserved }).ToArray()
        }).ToArray();

        public StockSegmentRecord[] SegmentHistory() => segmentHistory.Select(s => new StockSegmentRecord {
            segmentIndex = s.segmentIndex, companies = CopyShares(s.companies)
        }).ToArray();

        private void BeginBoardTurn()
        {
            DecisionOpen = false; aiRiseStreak = bioExpectationBps = 0; bioOccurred = pendingBioOccurred = false;
            aiUpProbabilityBps = pendingAiUpProbabilityBps = 5000; pendingBioExpectationBps = 0;
            segmentHistory.Clear();
            publicShares = CurrentShareMarket();
        }

        public void BeginDecision()
        {
            if (ProcessedDay >= 30 || ProcessedDay % 5 != 0 || allLocked) return;
            DecisionOpen = true;
        }

        // Every draw is a pure function of turn/segment/company; reconnect and developer rewind cannot reroll.
        private Random SegmentRandom(int segment, int salt) => new Random(unchecked(stockSeed * 397 ^ turn * 7919 ^ segment * 104729 ^ salt));

        public void RevealSegment()
        {
            if (!DecisionOpen || ProcessedDay >= 30) return;
            DecisionOpen = false;
            int start = ProcessedDay + 1, segment = ProcessedDay / 5;
            int Count(string id) => stockPlayers.Values.Sum(p => p.Shares[id]);
            foreach (string company in InvestmentRules.CompanyIds) foreach (string player in players) {
                int previous = publicShares.First(c => c.companyId == company).owners.First(o => o.playerId == player).shares;
                int delta = stockPlayers[player].Shares[company] - previous;
                if (delta != 0) fills[company].Add(new StockFill { playerId = player, day = ProcessedDay, quantity = delta });
            }
            publicShares = CurrentShareMarket();
            segmentHistory.Add(new StockSegmentRecord { segmentIndex = segment, companies = CopyShares(publicShares) });

            int aiInvestors = stockPlayers.Values.Count(p => p.Shares["AI"] > 0);
            int aiStart = market.ChangeBps("AI", start - 1);
            // Without investors the price/streak stay unchanged; next rise probability still accumulates.
            if (aiInvestors == 0) {
                market.SetSegment("AI", start, Enumerable.Repeat(aiStart, 5).ToArray());
                pendingAiUpProbabilityBps = AccumulateAiUpChance(aiUpProbabilityBps, 0, false);
            }
            else {
                int streak = Math.Min(5, aiRiseStreak);
                bool rising = SegmentRandom(segment, 11).Next(10000) < aiUpProbabilityBps;
                int rate = AiSegmentReturnBps(streak, Count("AI"), rising);
                int target = (int)Math.Round((10000m + aiStart) * (1m + rate / 10000m), MidpointRounding.AwayFromZero) - 10000;
                market.SetSegment("AI", start, Enumerable.Range(1, 5).Select(i => aiStart + (target - aiStart) * i / 5).ToArray());
                aiRiseStreak = rising ? aiRiseStreak + 1 : 0;
                pendingAiUpProbabilityBps = AccumulateAiUpChance(aiUpProbabilityBps, aiInvestors, !rising);
            }

            var bioRandom = SegmentRandom(segment, 23);
            bool lightning = !bioOccurred && bioRandom.Next(10000) < 500 + bioExpectationBps;
            int eventDay = lightning ? bioRandom.Next(5) : -1;
            int bioValue = market.ChangeBps("BIO", start - 1);
            var bio = new int[5];
            for (int i = 0; i < 5; i++) {
                // BIO lightning adds 200 percentage points; ordinary days alternate by one point.
                bioValue += i == eventDay ? 20000 : (start + i) % 2 == 1 ? 100 : -100;
                bio[i] = bioValue;
            }
            market.SetSegment("BIO", start, bio);
            pendingBioOccurred = bioOccurred || lightning;
            int bioCount = Count("BIO");
            pendingBioExpectationBps = pendingBioOccurred ? 0 : bioCount == 0 ? bioExpectationBps / 2
                : Math.Min(3500, bioExpectationBps + (bioCount == 1 ? 500 : bioCount == 2 ? 800 : 1000));
        }

        private void CompleteSegmentProbability()
        {
            aiUpProbabilityBps = pendingAiUpProbabilityBps;
            bioExpectationBps = pendingBioExpectationBps;
            bioOccurred = pendingBioOccurred;
        }

        private StockInsight[] BoardInsights() => new[] {
            new StockInsight { companyId = "AI", probabilityBps = 10000 - aiUpProbabilityBps, message = "",
                upProbabilityBps = aiUpProbabilityBps, downProbabilityBps = 10000 - aiUpProbabilityBps,
                probabilitySegment = ProcessedDay / 5 },
            new StockInsight { companyId = "BIO", probabilityBps = bioOccurred ? 0 : 500 + bioExpectationBps, message = "",
                upProbabilityBps = bioOccurred ? 0 : 500 + bioExpectationBps,
                downProbabilityBps = bioOccurred ? 10000 : 9500 - bioExpectationBps,
                probabilitySegment = ProcessedDay / 5 }
        };

        private Action CaptureBoardCheckpoint()
        {
            bool decision = DecisionOpen, occurred = bioOccurred, pendingOccurred = pendingBioOccurred;
            int ai = aiRiseStreak, bio = bioExpectationBps;
            int savedAiUp = aiUpProbabilityBps, savedPendingAiUp = pendingAiUpProbabilityBps, savedPendingBio = pendingBioExpectationBps;
            var shares = CopyShares(publicShares); var records = SegmentHistory();
            var prices = InvestmentRules.CompanyIds.ToDictionary(id => id, id => market.AllDays(id));
            return () => {
                aiUpProbabilityBps = savedAiUp; pendingAiUpProbabilityBps = savedPendingAiUp; pendingBioExpectationBps = savedPendingBio;
                DecisionOpen = decision; bioOccurred = occurred; pendingBioOccurred = pendingOccurred; aiRiseStreak = ai; bioExpectationBps = bio;
                publicShares = CopyShares(shares); segmentHistory.Clear();
                segmentHistory.AddRange(records.Select(r => new StockSegmentRecord { segmentIndex = r.segmentIndex, companies = CopyShares(r.companies) }));
                foreach (var price in prices) for (int start = 1; start <= 26; start += 5)
                    market.SetSegment(price.Key, start, price.Value.Skip(start).Take(5).ToArray());
            };
        }
    }
}
