using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.Rules
{
    public sealed partial class MarketService
    {
        public bool DecisionOpen { get; private set; }
        private int aiFallChanceBps, bioExpectationBps;
        private bool bioOccurred;
        private int[] foodBase;
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
            DecisionOpen = false; aiFallChanceBps = bioExpectationBps = 0; bioOccurred = false;
            foodBase = market.AllDays("FOOD"); segmentHistory.Clear();
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

            int foodReaction = Math.Min(2000, Count("FOOD") * 200);
            int foodValue = market.ChangeBps("FOOD", start - 1);
            var food = new int[5];
            for (int i = 0; i < 5; i++) {
                int move = foodBase[start + i] - foodBase[start + i - 1];
                foodValue += move + Math.Sign(move) * foodReaction;
                food[i] = foodValue;
            }
            market.SetSegment("FOOD", start, food);

            int aiCount = Count("AI");
            int aiInvestors = stockPlayers.Values.Count(p => p.Shares["AI"] > 0);
            // An empty segment breaks the rising streak before this segment's draw.
            if (aiCount == 0) aiFallChanceBps = 0;
            bool falling = aiInvestors >= 3 || SegmentRandom(segment, 11).Next(10000) < aiFallChanceBps;
            int aiMove = falling ? -3000 : 1500;
            int aiStart = market.ChangeBps("AI", start - 1);
            // 15/30 percentage points are the total move of one five-day segment.
            market.SetSegment("AI", start, Enumerable.Range(1, 5).Select(i => aiStart + aiMove * i / 5).ToArray());
            aiFallChanceBps = falling || aiCount == 0 ? 0 : Math.Min(10000, aiFallChanceBps + aiCount * 500);

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
            bioOccurred |= lightning;
            int bioCount = Count("BIO");
            bioExpectationBps = bioOccurred ? 0 : bioCount == 0 ? bioExpectationBps / 2
                : Math.Min(3500, bioExpectationBps + (bioCount == 1 ? 500 : bioCount == 2 ? 800 : 1000));
        }

        private StockInsight[] BoardInsights() => new[] {
            new StockInsight { companyId = "AI", probabilityBps = aiFallChanceBps,
                message = "하락 확률 " + (aiFallChanceBps / 100m).ToString("0.##") + "% · 투자 플레이어 3명 이상이면 무조건 하락 · 상승 시 투자말당 5% 누적 · 미투자 구간 0% 초기화 · 상승 +15%p / 하락 −30%p" },
            new StockInsight { companyId = "BIO", probabilityBps = bioOccurred ? 0 : 500 + bioExpectationBps,
                message = bioOccurred ? "Lightening 발생 완료 · 남은 구간 확률 0%" : "Lightening 확률 " + ((500 + bioExpectationBps) / 100m).ToString("0.##") + "% · 최대 40% · 발생 시 +200%p" }
        };

        private Action CaptureBoardCheckpoint()
        {
            bool decision = DecisionOpen, occurred = bioOccurred;
            int ai = aiFallChanceBps, bio = bioExpectationBps;
            var shares = CopyShares(publicShares); var records = SegmentHistory();
            var prices = InvestmentRules.CompanyIds.ToDictionary(id => id, id => market.AllDays(id));
            return () => {
                DecisionOpen = decision; bioOccurred = occurred; aiFallChanceBps = ai; bioExpectationBps = bio;
                publicShares = CopyShares(shares); segmentHistory.Clear();
                segmentHistory.AddRange(records.Select(r => new StockSegmentRecord { segmentIndex = r.segmentIndex, companies = CopyShares(r.companies) }));
                foreach (var price in prices) for (int start = 1; start <= 26; start += 5)
                    market.SetSegment(price.Key, start, price.Value.Skip(start).Take(5).ToArray());
            };
        }
    }
}
