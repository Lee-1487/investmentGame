using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.Bots
{
    public enum BotDifficulty { Easy, Normal, Hard }
    public enum BotPersonality { Adventurous, Stable, Strategic }
    public sealed class BotPolicy
    {
        public BotDifficulty Difficulty { get; }
        public int PurchaseBudget { get; }
        public int MaxIntervals { get; }
        public BotPersonality Personality { get; }
        public double PredictionNoise => Difficulty == BotDifficulty.Easy ? 1800 : Difficulty == BotDifficulty.Normal ? 650 : 120;
        public int TimingNoiseDays => Difficulty == BotDifficulty.Easy ? 3 : Difficulty == BotDifficulty.Normal ? 1 : 0;
        public double CompetitionNoise => Difficulty == BotDifficulty.Easy ? 1 : Difficulty == BotDifficulty.Normal ? .4 : .08;
        public double ReactionNoiseSeconds => Difficulty == BotDifficulty.Easy ? 4 : Difficulty == BotDifficulty.Normal ? 1.5 : .3;
        public double PatternAccuracy => Difficulty == BotDifficulty.Easy ? .25 : Difficulty == BotDifficulty.Normal ? .60 : .90;
        public double AiCautionWeight => Difficulty == BotDifficulty.Easy ? .35 : Difficulty == BotDifficulty.Normal ? .70 : 1.0;
        public double BaseAcceptExchangeProb => Difficulty == BotDifficulty.Easy ? .72 : Difficulty == BotDifficulty.Normal ? .65 : .60;
        public double AiRiskTolerance => Personality == BotPersonality.Adventurous ? .55 : Personality == BotPersonality.Strategic ? .25 : .10;
        public double BluffRumorProb => Personality == BotPersonality.Adventurous ? .35 : Personality == BotPersonality.Strategic ? .25 : .05;
        public BotPolicy(BotDifficulty difficulty, BotPersonality personality = BotPersonality.Strategic)
        {
            if (!Enum.IsDefined(typeof(BotDifficulty), difficulty)) throw new ArgumentOutOfRangeException(nameof(difficulty));
            if (!Enum.IsDefined(typeof(BotPersonality), personality)) throw new ArgumentOutOfRangeException(nameof(personality));
            Difficulty = difficulty; Personality = personality; PurchaseBudget = 3; MaxIntervals = 2;
        }
    }
    public sealed class ExchangeProposal
    {
        public string TargetPlayerId;
        public string OfferedCompanyId;
        public int OfferedStartDay;
        public string RequestedCompanyId;
        public int RequestedStartDay;
    }
    public sealed class RumorPlan
    {
        public string TargetPlayerId;
        public string CompanyId;
        public int EntryDay;
        public int ExitDay;
    }
    public sealed class InformationPurchase
    { public string CompanyId; public int BundleIndex, StartDay, EndDay; }
    // Only references Shared: no server services, fixture data, Unity or clock.
    public sealed class InformationBotStrategy
    {
        private readonly Random random;
        private readonly Dictionary<string, double> affinity = new Dictionary<string, double>();
        private readonly Dictionary<string, bool> turnRumorTrust = new Dictionary<string, bool>();
        private int rumorTrustTurn = -1;
        public BotPolicy Policy { get; }
        public InformationBotStrategy(int seed, BotPolicy policy)
        { random = new Random(seed); Policy = policy ?? throw new ArgumentNullException(nameof(policy)); }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
        public double GetAffinity(string playerId) => !string.IsNullOrEmpty(playerId) && affinity.TryGetValue(playerId, out double v) ? v : 0.50;
        private void AdjustAffinity(string playerId, double delta)
        {
            if (string.IsNullOrEmpty(playerId)) return;
            affinity[playerId] = Clamp01(GetAffinity(playerId) + delta);
        }
        public void RecordExchangeOutcome(string otherPlayerId, bool accepted)
            => AdjustAffinity(otherPlayerId, accepted ? 0.15 : -0.04);
        public void RecordSegmentOutcome(PlayerView view, PublicState state, int segmentIndex)
        {
            if (view == null || state == null) return;
            var record = state.stockSegmentHistory?.FirstOrDefault(s => s.segmentIndex == segmentIndex);
            if (record == null) return;
            int segStart = segmentIndex * 5 + 1, segEnd = segStart + 4;
            string self = view.recipientPlayerId;
            foreach (var intent in view.receivedTradeIntents ?? Array.Empty<TradeIntent>())
            {
                if (intent.targetPlayerId != self || intent.entryDay > segEnd || intent.exitDay < segStart) continue;
                int placed = record.companies?.FirstOrDefault(c => c.companyId == intent.companyId)?.owners
                    ?.FirstOrDefault(o => o.playerId == intent.senderId)?.shares ?? 0;
                AdjustAffinity(intent.senderId, placed > 0 ? 0.03 : -0.18);
            }
            foreach (string comp in new[] { "AI", "FOOD" })
            {
                var companyView = view.companies?.FirstOrDefault(c => c.companyId == comp);
                var days = KnownDays(companyView);
                int? startPrice = days[segStart - 1], endPrice = days[segEnd];
                if (!startPrice.HasValue || !endPrice.HasValue || endPrice.Value >= startPrice.Value) continue;
                var owners = record.companies?.FirstOrDefault(c => c.companyId == comp)?.owners ?? Array.Empty<ShareOwner>();
                int ownShares = owners.FirstOrDefault(o => o.playerId == self)?.shares ?? 0;
                if (ownShares <= 0) continue;
                double penaltyPerShare = comp == "AI" ? -0.12 : -0.07;
                foreach (var other in owners.Where(o => o.playerId != self && o.shares > 0))
                    AdjustAffinity(other.playerId, penaltyPerShare * other.shares);
            }
        }
        private bool TrustsRumor(int turn, string senderId)
        {
            if (rumorTrustTurn != turn) { rumorTrustTurn = turn; turnRumorTrust.Clear(); }
            if (!turnRumorTrust.TryGetValue(senderId, out bool trusted))
            {
                double prob = Clamp01(0.20 + GetAffinity(senderId) * 0.65 - (Policy.Difficulty == BotDifficulty.Hard ? 0.05 : 0.0));
                trusted = random.NextDouble() < prob;
                turnRumorTrust[senderId] = trusted;
            }
            return trusted;
        }
        private static bool HasWindowCard(PlayerView view, string companyId, int startDay)
        {
            var comp = view?.companies?.FirstOrDefault(c => c.companyId == companyId);
            return comp?.visibleWindows != null && comp.visibleWindows.Any(w => w.startDay == startDay && w.source != "PUBLIC" && w.source != "MARKET" && w.dailyChangesBps?.Length == 5);
        }
        public bool ShouldAcceptExchange(PlayerView view, InformationExchange request)
        {
            if (request == null || view == null) return false;
            // Reject if we don't own the requested card or already know the offered card.
            if (!HasWindowCard(view, request.requestedCompanyId, request.requestedStartDay)) return false;
            if (HasWindowCard(view, request.offeredCompanyId, request.offeredStartDay)) return false;
            double affinityMod = (GetAffinity(request.senderId) - 0.50) * 0.70;
            double personalityMod = Policy.Personality == BotPersonality.Adventurous ? 0.10 : Policy.Personality == BotPersonality.Stable ? -0.05 : 0.0;
            double skillMod = 0.0;
            if (Policy.Difficulty != BotDifficulty.Easy && view != null)
            {
                var comp = view.companies?.FirstOrDefault(c => c.companyId == request.offeredCompanyId);
                var days = KnownDays(comp);
                bool adjacent = (request.offeredStartDay > 1 && days[request.offeredStartDay - 1].HasValue)
                    || (request.offeredStartDay + 5 <= 30 && days[request.offeredStartDay + 5].HasValue);
                if (adjacent) skillMod += 0.10;
            }
            double prob = Math.Max(0.05, Math.Min(0.95, Policy.BaseAcceptExchangeProb + affinityMod + personalityMod + skillMod));
            return random.NextDouble() < prob;
        }
        public ExchangeProposal ChooseExchangeRequest(PlayerView view, PublicState state, ISet<string> triedTargets)
        {
            if (view == null || state == null) return null;
            var known = (view.companies ?? Array.Empty<VisibleCompany>())
                .Where(c => c.companyId == "FOOD" || c.companyId == "AUTO")
                .SelectMany(c => (c.visibleWindows ?? Array.Empty<VisibleInformationWindow>())
                    .Where(w => w.source != "PUBLIC" && w.source != "MARKET" && w.startDay >= 6 && w.startDay <= 26 && w.dailyChangesBps?.Length == 5)
                    .Select(w => new { Company = c.companyId, Start = w.startDay }))
                .GroupBy(x => x.Company + ":" + x.Start).Select(g => g.First()).ToArray();
            if (known.Length == 0) return null;
            var knownKeys = new HashSet<string>(known.Select(k => k.Company + ":" + k.Start));
            var unknown = new[] { "FOOD", "AUTO" }
                .SelectMany(comp => new[] { 6, 11, 16, 21, 26 }.Select(start => new { Company = comp, Start = start }))
                .Where(x => !knownKeys.Contains(x.Company + ":" + x.Start)).ToArray();
            if (unknown.Length == 0) return null;
            var knowledge = state.segmentKnowledge ?? Array.Empty<SegmentKnowledge>();
            var targets = (state.players ?? Array.Empty<Player>())
                .Select(p => p.playerId)
                .Where(id => !string.IsNullOrEmpty(id) && id != view.recipientPlayerId && (triedTargets == null || !triedTargets.Contains(id)))
                .Where(id => known.Any(o => unknown.Any(u => u.Company == o.Company && (knowledge.Length == 0
                    || knowledge.Any(k => k.companyId == u.Company && k.startDay == u.Start && (k.playerIds?.Contains(id) ?? false))))))
                .ToArray();
            if (targets.Length == 0) return null;
            double totalWeight = targets.Sum(t => Math.Max(0.05, GetAffinity(t)));
            double roll = random.NextDouble() * totalWeight, acc = 0;
            string chosenTarget = targets[targets.Length - 1];
            foreach (var t in targets) { acc += Math.Max(0.05, GetAffinity(t)); if (roll <= acc) { chosenTarget = t; break; } }
            var offerCandidates = known.Where(o => unknown.Any(u => u.Company == o.Company && (knowledge.Length == 0
                || knowledge.Any(k => k.companyId == u.Company && k.startDay == u.Start && (k.playerIds?.Contains(chosenTarget) ?? false))))
                && (knowledge.Length == 0 || !knowledge.Any(k => k.companyId == o.Company && k.startDay == o.Start && (k.playerIds?.Contains(chosenTarget) ?? false)))).ToArray();
            if (offerCandidates.Length == 0)
                offerCandidates = known.Where(o => unknown.Any(u => u.Company == o.Company)).ToArray();
            if (offerCandidates.Length == 0) return null;
            var offered = offerCandidates[random.Next(offerCandidates.Length)];
            var sameCompanyUnknown = unknown.Where(u => u.Company == offered.Company).ToArray();
            var targetKnown = sameCompanyUnknown.Where(u => knowledge.Length == 0 || knowledge.Any(k => k.companyId == u.Company && k.startDay == u.Start && (k.playerIds?.Contains(chosenTarget) ?? false))).ToArray();
            var wantedPool = targetKnown.Length > 0 ? targetKnown : sameCompanyUnknown;
            if (wantedPool.Length == 0) return null;
            var wanted = wantedPool.OrderByDescending(u => {
                double score = random.NextDouble() * (1.0 - Policy.PatternAccuracy);
                var comp = view.companies?.FirstOrDefault(c => c.companyId == u.Company);
                var days = KnownDays(comp);
                bool adj = (u.Start > 1 && days[u.Start - 1].HasValue) || (u.Start + 5 <= 30 && days[u.Start + 5].HasValue);
                if (adj) score += Policy.PatternAccuracy * 0.6;
                score += Policy.PatternAccuracy * (u.Start / 30.0) * 0.3;
                return score;
            }).First();
            return new ExchangeProposal
            {
                TargetPlayerId = chosenTarget,
                OfferedCompanyId = offered.Company,
                OfferedStartDay = offered.Start,
                RequestedCompanyId = wanted.Company,
                RequestedStartDay = wanted.Start
            };
        }
        public RumorPlan ChooseTradeIntent(PlayerView view, PublicState state, ISet<string> sentTargets = null)
        {
            if (view == null || state == null) return null;
            var targets = (state.players ?? Array.Empty<Player>()).Select(p => p.playerId)
                .Where(id => !string.IsNullOrEmpty(id) && id != view.recipientPlayerId && (sentTargets == null || !sentTargets.Contains(id))).ToArray();
            if (targets.Length == 0) return null;
            string target = targets[random.Next(targets.Length)];
            var ranks = RankStockCompanies(view, state);
            string intended = ranks.FirstOrDefault() ?? "AUTO";
            bool bluff = random.NextDouble() < Policy.BluffRumorProb + (GetAffinity(target) < 0.4 ? 0.20 : 0.0);
            string company = bluff ? (ranks.LastOrDefault() ?? "AI") : intended;
            int seg = random.Next(0, 6);
            return new RumorPlan { TargetPlayerId = target, CompanyId = company, EntryDay = seg * 5 + 1, ExitDay = seg * 5 + 5 };
        }

        private double Noise(double amplitude) => (random.NextDouble() * 2 - 1) * amplitude;
        public float NextReactionSeconds() => (float)(random.NextDouble() * Policy.ReactionNoiseSeconds);
        private double Preference(string id)
        {
            if (Policy.Personality == BotPersonality.Adventurous)
                return id == "AI" || id == "BIO" ? 700 : -300;
            if (Policy.Personality == BotPersonality.Stable)
                return id == "FOOD" || id == "AUTO" ? 900 : -600;
            return 0;
        }
        // Extrapolate only from this bot's visible samples, never the server's hidden chart.
        private double Forecast(VisibleCompany company, int day)
        {
            var days = KnownDays(company);
            day = Math.Max(0, Math.Min(30, day));
            if (days[day].HasValue) return days[day].Value;
            int left = day, right = day;
            while (left > 0 && !days[left].HasValue) left--;
            while (right < 30 && !days[right].HasValue) right++;
            if (days[left].HasValue && days[right].HasValue && right > left)
                return days[left].Value + (days[right].Value - days[left].Value) * (day - left) / (double)(right - left);
            // Better-skilled bots use multi-day trend across known samples instead of only the last 1-day step.
            int lookback = Policy.Difficulty == BotDifficulty.Hard ? 5 : Policy.Difficulty == BotDifficulty.Normal ? 3 : 1;
            int previous = Math.Max(0, left - lookback);
            while (previous < left && !days[previous].HasValue) previous++;
            if (previous == left) {
                previous = left - 1;
                while (previous >= 0 && !days[previous].HasValue) previous--;
            }
            double slope = previous >= 0 && previous < left && days[left].HasValue ? (days[left].Value - days[previous].Value) / (double)(left - previous) : 0;
            double blend = slope * Policy.PatternAccuracy + (random.NextDouble() * 2 - 1) * 150 * (1.0 - Policy.PatternAccuracy);
            return (days[left] ?? 0) + blend * Math.Min(5, day - left);
        }
        private double EstimateAiFallRisk(PlayerView view, PublicState state)
        {
            double chance = (view.portfolio?.insights?.FirstOrDefault(i => i.companyId == "AI")?.probabilityBps ?? 0) / 10000d;
            int previousOthers = state.shareMarket?.FirstOrDefault(c => c.companyId == "AI")?.owners
                ?.Where(o => o.playerId != view.recipientPlayerId).Sum(o => o.shares) ?? 0;
            int day = view.portfolio?.processedDay ?? 0;
            int segStart = day + 1, segEnd = Math.Min(30, segStart + 4);
            int trustedRumors = (view.receivedTradeIntents ?? Array.Empty<TradeIntent>())
                .Where(i => i.companyId == "AI" && i.entryDay <= segEnd && i.exitDay >= segStart && TrustsRumor(view.turn, i.senderId))
                .Select(i => i.senderId).Distinct().Count();
            double crowdFactor = Math.Max(0, (previousOthers + trustedRumors * 0.8 - 1.5) * 0.25) * Policy.AiCautionWeight;
            return Clamp01(Math.Max(chance, crowdFactor));
        }
        private double StockScore(PlayerView view, PublicState state, string id)
        {
            var company = view.companies?.FirstOrDefault(c => c.companyId == id);
            int day = view.portfolio?.processedDay ?? 0;
            int horizon = Math.Min(30, day + 5);
            double gain = Forecast(company, horizon) - Forecast(company, day);
            if (state.stockSystem && id == "AI") {
                double risk = EstimateAiFallRisk(view, state);
                double ev = (1 - risk) * 1500 - risk * 3000;
                if (Policy.Personality == BotPersonality.Adventurous) {
                    bool keepEntering = risk <= 0.15 || random.NextDouble() < (risk <= 0.45 ? 0.72 : 0.45);
                    gain = keepEntering ? Math.Max(600, ev + 900) : ev - 400;
                } else {
                    // Cautious entry: non-adventurous bots penalize AI proportionally to skill caution and risk above tolerance.
                    double excessRisk = Math.Max(0, risk - Policy.AiRiskTolerance);
                    gain = ev - excessRisk * 3500 * Policy.AiCautionWeight;
                }
            }
            if (state.stockSystem && id == "BIO") {
                double bioProb = (view.portfolio?.insights?.FirstOrDefault(i => i.companyId == id)?.probabilityBps ?? 500) / 10000d;
                gain = bioProb * 4000;
            }
            var market = state.shareMarket?.FirstOrDefault(c => c.companyId == id);
            double occupied = (market?.owners?.Where(o => o.playerId != view.recipientPlayerId).Sum(o => o.shares + o.reserved) ?? 0) / (double)StockRules.Supply;
            // Scarce shares receive an urgency premium, blurred by competition judgement noise.
            return gain + Preference(id) + 350 * Math.Max(0, occupied + Noise(Policy.CompetitionNoise)) + Noise(Policy.PredictionNoise);
        }
        public Dictionary<string, int> AllocateBoardTokens(PlayerView view, PublicState state)
        {
            var allocation = view.portfolio.holdings.ToDictionary(h => h.companyId, h => 0);
            var ranks = RankStockCompanies(view, state);
            if (ranks.Length == 0) return allocation;
            string top = ranks[0];
            if (top == "AI" && Policy.Personality != BotPersonality.Adventurous)
            {
                double risk = EstimateAiFallRisk(view, state);
                if (risk > Policy.AiRiskTolerance)
                {
                    int cautiousCap = random.NextDouble() < (Policy.Personality == BotPersonality.Stable ? 0.15 : 0.35) ? 1 : 0;
                    allocation[top] = cautiousCap;
                    if (ranks.Length > 1) allocation[ranks[1]] = 3 - cautiousCap;
                    return allocation;
                }
                allocation[top] = 2;
                if (ranks.Length > 1) allocation[ranks[1]] = 1;
                return allocation;
            }
            if (Policy.Personality == BotPersonality.Adventurous)
            {
                bool allIn = random.NextDouble() < 0.70 || ranks.Length == 1;
                allocation[top] = allIn ? 3 : 2;
                if (!allIn && ranks.Length > 1) allocation[ranks[1]] = 1;
            }
            else
            {
                allocation[top] = 2;
                if (ranks.Length > 1) allocation[ranks[1]] = 1;
            }
            return allocation;
        }
        private string rankingKey;
        private string[] ranking;
        public string[] RankStockCompanies(PlayerView view, PublicState state)
        {
            string key = view.turn + "|" + state.developerRevision + "|" + state.phase + "|" + state.selectionIndex + "|" + view.portfolio?.processedDay;
            if (rankingKey == key) return (string[])ranking.Clone();
            rankingKey = key;
            ranking = (state.shareMarket ?? Array.Empty<CompanyShareState>())
                .Select(c => new { Id = c.companyId, Score = StockScore(view, state, c.companyId) })
                .OrderByDescending(c => c.Score).ThenBy(c => c.Id).Select(c => c.Id).ToArray();
            return (string[])ranking.Clone();
        }
        public string SelectAugment(PlayerView view)
        {
            string[] preference = Policy.Personality == BotPersonality.Adventurous
                ? new[] { "BIO_RESEARCH", "CONCENTRATION", "LONG_TERM", "ANALYST", "HIDDEN_LOSS_CONTROL", "DIVERSIFY" }
                : Policy.Personality == BotPersonality.Stable
                ? new[] { "HIDDEN_LOSS_CONTROL", "DIVERSIFY", "LONG_TERM", "ANALYST", "CONCENTRATION", "BIO_RESEARCH" }
                : new[] { "ANALYST", "DIVERSIFY", "LONG_TERM", "CONCENTRATION", "HIDDEN_LOSS_CONTROL", "BIO_RESEARCH" };
            return preference.FirstOrDefault(id => view.portfolio.candidates.Contains(id)) ?? view.portfolio.candidates.FirstOrDefault();
        }
        private sealed class StockDecision { public bool Buy; public float ReadyAt; }
        private readonly Dictionary<string, StockDecision> stockDecisions = new Dictionary<string, StockDecision>();
        private int decisionTurn = -1, decisionRevision = -1;
        public int StockTarget(PlayerView view, PublicState state, string id, float now)
        {
            var portfolio = view.portfolio;
            var holding = portfolio.holdings.First(h => h.companyId == id);
            var market = state.shareMarket.First(c => c.companyId == id);
            int day = portfolio.processedDay;
            if (decisionTurn != view.turn || decisionRevision != state.developerRevision) {
                stockDecisions.Clear(); decisionTurn = view.turn; decisionRevision = state.developerRevision;
            }
            string key = id + ":" + day;
            if (!stockDecisions.TryGetValue(key, out var decision)) {
                int offset = random.Next(-Policy.TimingNoiseDays, Policy.TimingNoiseDays + 1);
                var company = view.companies.FirstOrDefault(c => c.companyId == id);
                int horizon = Math.Min(30, day + 3 + offset);
                double gain = Forecast(company, horizon) - Forecast(company, day);
                double occupied = (market.owners?.Where(o => o.playerId != view.recipientPlayerId).Sum(o => o.shares + o.reserved) ?? 0) / (double)StockRules.Supply;
                double urgency = gain > 0 ? 350 * Math.Max(0, occupied + Noise(Policy.CompetitionNoise)) : 0;
                double hiddenBias = day >= 25 ? (Policy.Personality == BotPersonality.Adventurous ? 700
                    : Policy.Personality == BotPersonality.Stable && id != "FOOD" && id != "AUTO" ? -1400 : 0) : 0;
                decision = new StockDecision { Buy = gain + Preference(id) + hiddenBias + urgency + Noise(Policy.PredictionNoise) > 0,
                    ReadyAt = now + NextReactionSeconds() };
                stockDecisions.Add(key, decision);
            }
            if (now < decision.ReadyAt) return holding.activeShares;
            return decision.Buy ? holding.activeShares + portfolio.AvailableToBuy(market) : 0;
        }

        public static int?[] KnownDays(VisibleCompany company)
        {
            return InformationWindowRules.KnownDays(company);
        }
        public InformationPurchase NextPublicChoice(PlayerView view, PublicState state)
        {
            foreach (var company in view.companies.OrderBy(c => (state.publicInformation ?? Array.Empty<PublicInformationChoice>()).Count(w => w.companyId == c.companyId)))
            {
                if (state.stockSystem) {
                    if (InformationWindowRules.CanRevealPublic(state, company.companyId)) return new InformationPurchase { CompanyId = company.companyId };
                    continue;
                }
                for (int start = 1; start <= 26; start++)
                    if (!InformationWindowRules.OverlapsPublic(state, company.companyId, start))
                        return new InformationPurchase { CompanyId = company.companyId, StartDay = start };
            }
            return null;
        }
        public InformationPurchase NextRandomPurchase(PlayerView view, LobbySettings settings)
        {
            if (view == null || view.coins <= 0) return null;
            var options = settings ?? new LobbySettings();
            int first = Math.Max(1, options.informationStartDay);
            var candidates = (view.companies ?? Array.Empty<VisibleCompany>())
                .Select(c => new { Company = c.companyId, Unknown = Enumerable.Range(first, Math.Max(0, options.informationEndDay - first + 1))
                    .Count(day => !InformationWindowRules.KnownDays(c)[day].HasValue) }).Where(c => c.Unknown > 0).ToArray();
            if (candidates.Length == 0) return null;
            var choices = candidates.OrderByDescending(c => c.Unknown * 100 + Preference(c.Company) + Noise(Policy.PredictionNoise)).Take(1).ToArray();
            return new InformationPurchase { CompanyId = choices[random.Next(choices.Length)].Company };
        }
        public InformationPurchase NextPurchase(PlayerView view, ISet<string> attempted)
        {
            if (view == null || view.coins <= 0 && (view.ability?.freeInformationCredits ?? 0) == 0) return null;
            var candidates = new List<Tuple<InformationPurchase, int>>();
            foreach (var company in view.companies ?? Array.Empty<VisibleCompany>())
            {
                var days = KnownDays(company);
                if (company.visibleWindows != null)
                {
                    for (int start = 1; start <= 30; start++)
                    {
                        if (attempted.Contains(company.companyId + ":" + start)) continue;
                        int budget = view.coins + (view.ability?.freeInformationCredits ?? 0);
                        int end = Math.Min(30, start + Math.Min(5, budget) - 1);
                        int newDays = InformationWindowRules.NewDays(company, start, end);
                        if (newDays == 0) continue;
                        int potential = newDays;
                        for (int d = start; d <= end; d++)
                            if (!days[d].HasValue)
                                for (int other = 0; other <= 30; other++)
                                    if (days[other].HasValue && Math.Abs(d - other) >= 5 && Math.Abs(d - other) <= 10) potential++;
                        candidates.Add(Tuple.Create(new InformationPurchase { CompanyId = company.companyId, StartDay = start, EndDay = end }, potential));
                    }
                    continue;
                }
                for (int b = 0; b < 6; b++)
                {
                    if ((company.visibleBundles ?? Array.Empty<VisibleBundle>()).Any(v => v.bundleIndex == b) || attempted.Contains(company.companyId + ":" + b)) continue;
                    int potential = 0;
                    for (int day = b * 5 + 1; day <= b * 5 + 5; day++)
                        for (int other = 0; other <= 30; other++)
                            if (days[other].HasValue && Math.Abs(day - other) >= 5 && Math.Abs(day - other) <= 10) potential++;
                    candidates.Add(Tuple.Create(new InformationPurchase { CompanyId = company.companyId, BundleIndex = b }, potential));
                }
            }
            if (candidates.Count == 0) return null;
            int best = candidates.Max(c => c.Item2);
            var ties = candidates.Where(c => c.Item2 == best).ToArray();
            return ties[random.Next(ties.Length)].Item1;
        }
        private sealed class Plan
        { public string Company; public long Score; public Interval[] Intervals; }
        private Plan Evaluate(PlayerView view, string companyId)
        {
            var days = KnownDays(view?.companies?.FirstOrDefault(c => c.companyId == companyId));
            var entryValues = view?.entryAtOpen == true ? InformationWindowRules.KnownOpens(view.companies?.FirstOrDefault(c => c.companyId == companyId)) : days;
            var candidates = new List<Plan>();
            for (int entry = 0; entry <= (view?.entryAtOpen == true ? 26 : 25); entry++)
                for (int exit = entry + 5 - (view?.entryAtOpen == true && entry > 0 ? 1 : 0); exit <= Math.Min(30, entry + 10 - (view?.entryAtOpen == true && entry > 0 ? 1 : 0)); exit++)
                    if (entryValues[entry].HasValue && days[exit].HasValue)
                        candidates.Add(new Plan { Company = companyId, Score = (long)(days[exit].Value - entryValues[entry].Value + Noise(Policy.PredictionNoise)),
                            Intervals = new[] { new Interval { entryDay = entry, exitDay = exit } } });
            if (candidates.Count == 0) return new Plan { Company = companyId, Score = 0,
                Intervals = new[] { new Interval { entryDay = 0, exitDay = 5 } } };
            var best = candidates.OrderByDescending(c => c.Score).First();
            if (Policy.MaxIntervals == 2)
                foreach (var a in candidates)
                    foreach (var b in candidates)
                        if ((a.Intervals[0].exitDay < b.Intervals[0].entryDay || view?.entryAtOpen != true && a.Intervals[0].exitDay == b.Intervals[0].entryDay) && a.Score + b.Score > best.Score)
                            best = new Plan { Company = companyId, Score = a.Score + b.Score, Intervals = new[] { a.Intervals[0], b.Intervals[0] } };
            return best;
        }
        public string SelectCompany(PlayerView view, PublicState state)
        {
            var own = view?.selectedCompanies ?? Array.Empty<string>();
            var plans = (state?.companySeats ?? Array.Empty<CompanySeat>())
                .Where(s => s.remainingSeats > 0 && !own.Contains(s.companyId)).Select(s => Evaluate(view, s.companyId)).ToArray();
            if (plans.Length == 0) return null;
            foreach (var plan in plans) plan.Score += (long)(Preference(plan.Company) + Noise(Policy.PredictionNoise));
            long best = plans.Max(p => p.Score);
            var ties = plans.Where(p => p.Score == best).ToArray();
            return ties[random.Next(ties.Length)].Company;
        }
        public Investment[] BuildOrder(PlayerView view, bool fallback = false)
        {
            var selected = view?.selectedCompanies;
            if (selected == null || selected.Length != 2 || selected[0] == selected[1]) return null;
            var plans = selected.Select(c => Evaluate(view, c)).ToArray();
            if (!fallback) plans = plans.OrderByDescending(p => p.Score).ToArray();
            int firstWeight = !fallback && plans[0].Score > plans[1].Score ? 7000 : 5000;
            return plans.Select((p, i) => new Investment { companyId = p.Company, weightBps = i == 0 ? firstWeight : 10000 - firstWeight,
                intervals = fallback ? new[] { new Interval { entryDay = 0, exitDay = 5 } } : p.Intervals.Select(t =>
                    new Interval { entryDay = t.entryDay, exitDay = t.exitDay }).ToArray() }).ToArray();
        }
    }
}
