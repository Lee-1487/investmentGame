using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.Rules
{
    public sealed partial class MarketService : IStockMarketSystem
    {
        private sealed class StockPlayer
        {
            public readonly Dictionary<string, int> Shares = InvestmentRules.CompanyIds.ToDictionary(c => c, c => 0);
            public readonly Dictionary<string, int> Streak = InvestmentRules.CompanyIds.ToDictionary(c => c, c => 0);
            public readonly List<string> Augments = new List<string>();
            public string[] Candidates = new string[0];
            public bool Chosen = true, PriorityUsed;
            public string Focus;
            public int Discount, OpportunityTurn, PendingDiscount;
        }
        private readonly Dictionary<string, StockPlayer> stockPlayers = new Dictionary<string, StockPlayer>();
        private readonly Dictionary<int, Dictionary<string, Dictionary<string, int>>> stockHistory = new Dictionary<int, Dictionary<string, Dictionary<string, int>>>();
        private int stockSeed;
        public bool StockEnabled { get; private set; }
        public static MarketService CreateStockSystem(int seed)
        {
            var rules = new InvestmentRules { entryAtOpen = true, minHoldingDays = 1, maxHoldingDays = 30, maxIntervalsPerCompany = 0 };
            return new MarketService(t => GenerateStockMarket(seed, t), rules) { StockEnabled = true, stockSeed = seed, UsesWindowInformation = true };
        }
        private static uint StockHash(int seed, int turn, uint salt)
        {
            unchecked { uint x = (uint)seed ^ (uint)turn * 2654435761U ^ salt; x ^= x >> 16; x *= 2246822519U; x ^= x >> 13; x *= 3266489917U; return x ^ (x >> 16); }
        }
        public static int BioSuccessChance(int seed, int turn) => 3500 + (int)(StockHash(seed, turn, 17) % 4001);
        private static MarketData GenerateStockMarket(int seed, int turn)
        {
            var data = PatternMarketGenerator.Generate(seed, turn);
            var bio = data.companies.Single(c => c.companyId == "BIO");
            int effect = StockHash(seed, turn, 53) % 10000 < BioSuccessChance(seed, turn) ? 1800 : -1600;
            var raw = bio.dailyChangesBps.Select((v, d) => v + effect * Math.Max(0, d - 12) / 18.0).ToArray();
            double scale = 1; int limit = RandomMarketGenerator.DailyLimitBps("BIO", turn);
            for (int d = 1; d <= 30; d++)
            {
                if (raw[d] != 0) scale = Math.Min(scale, 7600 / Math.Abs(raw[d]));
                if (raw[d] != raw[d - 1]) scale = Math.Min(scale, (limit - 1) / Math.Abs(raw[d] - raw[d - 1]));
            }
            bio.dailyChangesBps = raw.Select(v => (int)Math.Round(v * scale)).ToArray();
            return data;
        }
        private Dictionary<string, int> StockHoldings(string player)
            => stockPlayers.TryGetValue(player, out var p) ? p.Shares : new Dictionary<string, int>();
        private bool HasStockAugment(string player, string id) => stockPlayers[player].Augments.Contains(id);
        private void BeginStockTurn()
        {
            foreach (string id in players)
            {
                if (!stockPlayers.TryGetValue(id, out var p)) stockPlayers.Add(id, p = new StockPlayer());
                p.Discount = p.PendingDiscount + (p.Augments.Contains("ANALYST") ? 2 : 0); p.PendingDiscount = 0;
                p.Chosen = true; p.Candidates = new string[0];
            }
        }
        public CompanyShareState[] GetShareMarket()
            => InvestmentRules.CompanyIds.Select(id => new CompanyShareState { companyId = id,
                owners = players.Select(p => new ShareOwner { playerId = p, shares = StockHoldings(p).TryGetValue(id, out var q) ? q : 0 }).ToArray() }).ToArray();
        public PortfolioState GetPortfolio(string playerId)
        {
            if (!stockPlayers.TryGetValue(playerId, out var p)) return new PortfolioState { holdings = new PortfolioHolding[0], augments = new string[0], candidates = new string[0], insights = new StockInsight[0] };
            return new PortfolioState { usedSlots = p.Shares.Values.Sum(), informationDiscount = p.Discount, augmentChosen = p.Chosen,
                priorityUsed = p.PriorityUsed, concentrationCompany = p.Focus, augments = p.Augments.ToArray(), candidates = (string[])p.Candidates.Clone(),
                holdings = p.Shares.Select(v => new PortfolioHolding { companyId = v.Key, shares = v.Value, consecutiveTurns = p.Streak[v.Key] }).ToArray(),
                insights = p.Augments.Contains("BIO_RESEARCH") ? new[] { new StockInsight { companyId = "BIO", probabilityBps = BioSuccessChance(stockSeed, turn),
                    message = "BIO 연구 이벤트 성공 가능성 · 최종 수익 보장 아님" } } : new StockInsight[0] };
        }
        public RuleResult TradeShares(RequestContext c, ShareOrder[] requested)
        {
            string error = CheckContext(c, Protocol.Selection); if (error != "") return new RuleResult(error);
            if (allLocked) return new RuleResult("ORDER_LOCKED");
            var p = stockPlayers[c.PlayerId];
            if (requested == null || requested.Length == 0 || requested.Length > (p.Augments.Contains("FLEXIBLE") ? 2 : 1) || requested.Any(v => v == null)) return new RuleResult("INVALID_SHARE_ORDER");
            if (requested.Select(v => v.companyId).Distinct().Count() != requested.Length) return new RuleResult("DUPLICATE_COMPANY");
            var candidate = new Dictionary<string, int>(p.Shares);
            foreach (var v in requested)
            {
                if (v.companyId == null || !candidate.ContainsKey(v.companyId)) return new RuleResult("INVALID_COMPANY");
                int cap = p.Focus == v.companyId ? 12 : StockRules.PersonalLimit;
                if (v.targetShares < 0 || v.targetShares > cap) return new RuleResult("SHARE_LIMIT");
                candidate[v.companyId] = v.targetShares;
            }
            if (candidate.Values.Sum() > StockRules.Slots) return new RuleResult("PORTFOLIO_FULL");
            if (candidate.All(v => v.Value == p.Shares[v.Key])) return new RuleResult("NO_SHARE_CHANGE");
            foreach (var v in requested)
                if (players.Where(id => id != c.PlayerId).Sum(id => stockPlayers[id].Shares[v.companyId]) + v.targetShares > StockRules.Supply)
                {
                    if (p.Augments.Contains("OPPORTUNITY") && p.OpportunityTurn != turn)
                    { p.OpportunityTurn = turn; p.Discount = Math.Min(4, p.Discount + 2); p.PendingDiscount = 2; }
                    return new RuleResult("INSUFFICIENT_SHARES");
                }
            foreach (var v in requested)
            { p.Shares[v.companyId] = v.targetShares; if (v.targetShares == 0) p.Streak[v.companyId] = 0; }
            return new RuleResult();
        }
        public void BeginAugmentSelection()
        {
            foreach (var id in players)
            {
                var p = stockPlayers[id];
                var random = new Random(unchecked(stockSeed ^ turn * 7919 ^ Array.IndexOf(players, id) * 397));
                p.Candidates = StockRules.Augments.Where(a => !p.Augments.Contains(a)).OrderBy(a => random.Next()).Take(3).ToArray();
                p.Chosen = false;
            }
        }
        public RuleResult ChooseAugment(RequestContext c, string id, string company)
        {
            string error = CheckContext(c, StockRules.AugmentPhase); if (error != "") return new RuleResult(error);
            var p = stockPlayers[c.PlayerId];
            if (p.Chosen) return new RuleResult("AUGMENT_ALREADY_CHOSEN");
            if (id == null || !p.Candidates.Contains(id)) return new RuleResult("INVALID_AUGMENT");
            if (id == "CONCENTRATION" && !InvestmentRules.CompanyIds.Contains(company)) return new RuleResult("INVALID_COMPANY");
            p.Augments.Add(id); p.Chosen = true;
            if (id == "CONCENTRATION") p.Focus = company;
            return new RuleResult();
        }
        public bool AllAugmentsChosen => players.All(id => stockPlayers[id].Chosen);
        public void CompleteAugmentSelection()
        {
            foreach (var id in players.Where(id => !stockPlayers[id].Chosen))
                ChooseAugment(new RequestContext("", turn, StockRules.AugmentPhase, id), stockPlayers[id].Candidates[0], "AI");
        }
        public RuleResult ConsumePriority(string playerId)
        {
            var p = stockPlayers[playerId];
            if (!p.Augments.Contains("PRIORITY") || p.PriorityUsed) return new RuleResult("PRIORITY_UNAVAILABLE");
            p.PriorityUsed = true; return new RuleResult();
        }
        public void LockPortfolios()
        {
            if (allLocked) return;
            foreach (var id in players)
                orders[id] = stockPlayers[id].Shares.Where(v => v.Value > 0).Select(v => new Investment { companyId = v.Key,
                    weightBps = v.Value * 10000 / StockRules.Slots, intervals = new[] { new Interval { entryDay = 0, exitDay = 30 } } }).ToArray();
            stockHistory[turn] = players.ToDictionary(id => id, id => new Dictionary<string, int>(stockPlayers[id].Shares));
            allLocked = true;
        }
        private int StockSharesForHistory(string player, string company, int requestedTurn)
            => stockHistory.TryGetValue(requestedTurn, out var snapshot) ? snapshot[player][company] : stockPlayers[player].Shares[company];
        private int StockCompanyRate(string player, string company, int raw)
        {
            var p = stockPlayers[player]; int rate = Math.Max(-10000, raw);
            if (p.Augments.Contains("DIVERSIFY") && p.Shares.Values.Count(q => q >= 3) >= 3 && rate < 0) rate = rate * 80 / 100;
            if (p.Augments.Contains("SPACE_PIONEER") && company == "SPACE" && p.Streak[company] + 1 >= 2) rate += 200;
            if (p.Augments.Contains("LONG_TERM") && p.Streak[company] + 1 >= 3) rate += 200;
            return rate;
        }
        private PlayerSettlement SettleStockPlayer(string player)
        {
            long investable = turnStartAssets[player] - costs[player], allocatedTotal = 0, profit = 0;
            var p = stockPlayers[player];
            var result = new PlayerSettlement { playerId = player, startingAssets = turnStartAssets[player] };
            foreach (var v in p.Shares.Where(v => v.Value > 0))
            {
                long allocated = investable * v.Value / StockRules.Slots;
                int rate = StockCompanyRate(player, v.Key, market.ChangeBps(v.Key, 30));
                long amount = Settlement.Profit(allocated, rate); profit += amount; allocatedTotal += allocated;
                result.companyProfits.Add(new CompanyProfit { companyId = v.Key, allocatedAssets = allocated, profit = amount, returnBps = rate });
            }
            if (p.Augments.Contains("CASH_INTEREST")) profit += Math.Min((investable - allocatedTotal) * 200 / 10000, investable / 100);
            result.endingAssets = Math.Max(0, investable + profit); result.profit = result.endingAssets - result.startingAssets;
            result.turnReturnBps = Settlement.ReturnBps(result.startingAssets, result.endingAssets);
            return result;
        }
        public int InformationDiscount(string playerId) => stockPlayers.TryGetValue(playerId, out var p) ? p.Discount : 0;
        public void ConsumeInformationDiscount(string playerId, int days) => stockPlayers[playerId].Discount -= days;
        public VisibleInformationWindow[] AnalysisWindows(string playerId)
        {
            if (turn <= 1 || !stockPlayers.TryGetValue(playerId, out var p)) return new VisibleInformationWindow[0];
            var previous = markets[turn - 1];
            return InvestmentRules.CompanyIds.Where(id =>
                p.Augments.Contains("CONTRARIAN") && previous.ChangeBps(id, 30) < 0 ||
                p.Augments.Contains("TREND") && Enumerable.Range(28, 3).All(d => previous.ChangeBps(id, d) > previous.ChangeBps(id, d - 1)))
                .Select(id => new VisibleInformationWindow { companyId = id, recordId = "augment-" + turn + "-" + playerId + "-" + id, source = "AUGMENT", startDay = 1,
                    dailyChangesBps = GetWindow(turn, id, 1, 3), dailyOpenBps = GetOpenWindow(turn, id, 1, 3) }).ToArray();
        }
    }
}
