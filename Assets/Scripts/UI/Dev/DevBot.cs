using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Bots;
using BotDifficulty = BlackWhale.Bots.BotDifficulty;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using BlackWhale.UI.State;

namespace BlackWhale.UI.Dev
{
    // Host-owned lifetime, but only the same private/public messages as a human client.
    public sealed class DevBot
    {
        private readonly GameClientController controller;
        private readonly float thinkSeconds;
        private readonly int seed;
        private InformationBotStrategy strategy;
        private BotDifficulty difficulty;
        private BotPersonality personality;
        private float actionDelay;
        private readonly PhaseClock clock = new PhaseClock();
        private PublicState syncedState;
        private string stateKey = "";
        private int currentTurn = -1, currentRevision = -1, purchases, failures, orderAttempts, exchangeAttempts, rumorAttempts;
        private float since;
        private readonly HashSet<string> attempted = new HashSet<string>();
        private readonly HashSet<string> triedExchangeTargets = new HashSet<string>();
        private readonly HashSet<string> sentRumorTargets = new HashSet<string>();
        private readonly HashSet<string> seenExchangeOutcomes = new HashSet<string>();
        private readonly HashSet<string> seenSegments = new HashSet<string>();
        private string boardAllocationKey;
        private Dictionary<string, int> boardAllocation;
        public IGameClient Client { get; }
        public GameClientController Controller => controller;
        public DevBot(IGameClient client, float thinkSeconds, int seed = 1, BotDifficulty difficulty = BotDifficulty.Normal, BotPersonality personality = BotPersonality.Strategic)
        {
            Client = client; controller = new GameClientController(client); this.thinkSeconds = thinkSeconds;
            this.seed = seed; this.difficulty = difficulty; this.personality = personality;
            strategy = new InformationBotStrategy(seed, new BotPolicy(difficulty, personality));
            Client.MessageReceived += OnResult;
        }
        public void Dispose() { Client.MessageReceived -= OnResult; controller.Dispose(); Client.Dispose(); }
        private void OnResult(ServerMessage message)
        {
            var r = message.type == "COMMAND_RESULT" ? message.response : null;
            if (r == null || r.turn != currentTurn) return;
            if ((r.type == "BUY_INFORMATION_RESULT" || r.type == "BUY_INFORMATION_WINDOW_RESULT") && !r.success) failures++;
        }
        public bool SetDifficulty(BotDifficulty value)
        {
            if (controller.Store.Public?.phase != Protocol.Lobby) return false;
            difficulty = value; strategy = new InformationBotStrategy(seed, new BotPolicy(value, personality)); return true;
        }
        public void Update(float now)
        {
            var s = controller.Store.Public;
            if (s == null || !string.IsNullOrEmpty(controller.Store.EndReason)) return;
            if (s.paused) { since = now; return; }
            if (!ReferenceEquals(syncedState, s)) { syncedState = s; clock.Sync(s.serverElapsedSeconds, now); }
            {
                // The server freezes profiles at game start; also consume the first in-game snapshot.
                var profile = s.players?.FirstOrDefault(p => p.playerId == controller.LocalPlayerId && p.isBot);
                if (profile != null && Enum.TryParse(profile.botDifficulty, out BotDifficulty selected) &&
                    Enum.TryParse(profile.botPersonality, out BotPersonality style) && (selected != difficulty || style != personality)) {
                    personality = style; difficulty = selected; strategy = new InformationBotStrategy(seed, new BotPolicy(selected, style));
                }
            }
            if (currentTurn != s.turn || currentRevision != s.developerRevision) {
                currentTurn = s.turn; currentRevision = s.developerRevision;
                purchases = failures = orderAttempts = exchangeAttempts = rumorAttempts = 0;
                attempted.Clear(); triedExchangeTargets.Clear(); sentRumorTargets.Clear(); seenExchangeOutcomes.Clear(); seenSegments.Clear();
            }
            string key = s.turn + "|" + s.phase + "|" + s.selectionIndex + "|" + s.currentPlayerId + "|" + s.developerRevision;
            if (key != stateKey) { stateKey = key; since = now; actionDelay = s.stockSystem ? strategy.NextReactionSeconds() : 0; }
            if (now - since < thinkSeconds + (s.stockSystem && s.phase == Protocol.Investment ? 0 : actionDelay)) return;
            var view = controller.Store.View;
            if (s.phase != Protocol.Lobby && (view == null || view.turn != s.turn)) return;
            if (s.stockSystem && s.phase == StockRules.AugmentPhase)
            {
                if (view?.portfolio?.augmentChosen == false && view.portfolio.candidates?.Length > 0)
                    controller.ChooseAugment(strategy.SelectAugment(view), strategy.RankStockCompanies(view, s).FirstOrDefault() ?? "AI");
                return;
            }
            if (s.stockSystem && s.phase == Protocol.Selection)
            {
                if (!controller.CanDraftShares()) return;
                var p = view.portfolio;
                var ranks = strategy.RankStockCompanies(view, s);
                var ranked = ranks.Select(id => s.shareMarket.First(c => c.companyId == id));
                int budget = Math.Min(p.CashSlots, p.draftSharesRemaining);
                var shareOrders = new System.Collections.Generic.List<ShareOrder>();
                foreach (var c in ranked)
                {
                    int own = p.holdings.First(v => v.companyId == c.companyId).shares;
                    int remaining = StockRules.Supply - c.owners.Sum(v => v.shares + v.reserved);
                    int added = Math.Max(0, Math.Min(p.LimitFor(c.companyId) - own, Math.Min(remaining, budget)));
                    if (added == 0) continue;
                    shareOrders.Add(new ShareOrder { companyId = c.companyId, targetShares = own + added }); budget -= added;
                }
                if (shareOrders.Count > 0) { controller.TradeShares(shareOrders.ToArray()); since = now; return; }
                controller.PassStockDraft(); since = now; return;
            }
            if (s.stockSystem && view != null) {
                foreach (var ex in view.informationExchanges ?? Array.Empty<InformationExchange>()) {
                    if ((ex.status == "ACCEPTED" || ex.status == "REJECTED") && seenExchangeOutcomes.Add(ex.id + ":" + ex.status)) {
                        string other = ex.senderId == controller.LocalPlayerId ? ex.targetPlayerId : ex.senderId;
                        strategy.RecordExchangeOutcome(other, ex.status == "ACCEPTED");
                    }
                }
                if (s.phase == Protocol.Investment && !s.stockSegmentRevealing) {
                    foreach (var rec in s.stockSegmentHistory ?? Array.Empty<StockSegmentRecord>())
                        if (seenSegments.Add(s.turn + ":" + rec.segmentIndex))
                            strategy.RecordSegmentOutcome(view, s, rec.segmentIndex);
                }
            }
            if (s.stockSystem && s.phase == Protocol.Investment)
            {
                if (s.stockSegmentRevealing) return;
                if (controller.Store.HasPending(StockRules.Position)) return;
                string allocationKey = s.turn + ":" + s.stockSegmentIndex + ":" + s.developerRevision;
                if (boardAllocationKey != allocationKey) {
                    boardAllocationKey = allocationKey;
                    boardAllocation = strategy.AllocateBoardTokens(view, s);
                    foreach (var holding in view.portfolio.holdings.Where(h => !h.canBuy))
                        boardAllocation[holding.companyId] = Math.Min(boardAllocation[holding.companyId], holding.shares);
                }
                // Release excess tokens before moving them to another company.
                var change = view.portfolio.holdings.Where(h => h.shares != boardAllocation[h.companyId])
                    .OrderBy(h => boardAllocation[h.companyId] - h.shares).FirstOrDefault();
                if (change != null) controller.SetStockPosition(change.companyId, boardAllocation[change.companyId]);
                since = now; return;
            }
            if (s.stockSystem && s.phase == Protocol.Info) {
                if (controller.Store.HasPending(StockRules.ShareInformation) || controller.Store.HasPending(StockRules.RespondExchange)
                    || controller.Store.HasPending(StockRules.SendPlan)) return;
                var incoming = (view.informationExchanges ?? Array.Empty<InformationExchange>())
                    .FirstOrDefault(e => e.status == "PENDING" && e.targetPlayerId == controller.LocalPlayerId);
                if (incoming != null) {
                    controller.RespondInformationExchange(incoming.id, strategy.ShouldAcceptExchange(view, incoming));
                    since = now; return;
                }
                if (controller.IsDoneForCurrentPhase) return;
                bool hasOutgoingPending = (view.informationExchanges ?? Array.Empty<InformationExchange>())
                    .Any(e => e.status == "PENDING" && e.senderId == controller.LocalPlayerId);
                if (!hasOutgoingPending && exchangeAttempts < 3) {
                    var req = strategy.ChooseExchangeRequest(view, s, triedExchangeTargets);
                    if (req != null) {
                        exchangeAttempts++;
                        triedExchangeTargets.Add(req.TargetPlayerId);
                        controller.ShareInformation(req.TargetPlayerId, req.OfferedCompanyId, req.OfferedStartDay, req.RequestedCompanyId, req.RequestedStartDay);
                        since = now; return;
                    }
                    exchangeAttempts = 3;
                }
                if (rumorAttempts < 2) {
                    var rumor = strategy.ChooseTradeIntent(view, s, sentRumorTargets);
                    if (rumor != null) {
                        rumorAttempts++;
                        sentRumorTargets.Add(rumor.TargetPlayerId);
                        controller.SendTradeIntent(rumor.TargetPlayerId, rumor.CompanyId, rumor.EntryDay, rumor.ExitDay);
                        since = now; return;
                    }
                    rumorAttempts = 2;
                }
                bool anyPendingExchange = (view.informationExchanges ?? Array.Empty<InformationExchange>())
                    .Any(e => e.status == "PENDING" && (e.senderId == controller.LocalPlayerId || e.targetPlayerId == controller.LocalPlayerId));
                if (!anyPendingExchange && controller.CanFinishPhase()) controller.FinishPhase();
                return;
            }
            switch (s.phase)
            {
                case Protocol.Lobby:
                    if (!controller.IsLocalReady && !controller.Store.HasPending(CommandTypes.SetReady)) controller.SetReady(true);
                    else if (controller.IsHost && controller.AllPlayersReady && !controller.Store.HasPending(CommandTypes.StartGame)) controller.StartGame();
                    break;
                case Protocol.AbilitySelection:
                    if (string.IsNullOrEmpty(view.ability?.selectedId) && view.ability?.candidates?.Length > 0 && !controller.Store.HasPending("SELECT_ABILITY"))
                        controller.SelectAbility(view.ability.candidates[0]);
                    break;
                case Protocol.Auction:
                    if (view.auction != null && !view.auction.confirmed && !controller.Store.HasPending("SUBMIT_BID")) controller.SubmitBid(0);
                    break;
                case Protocol.Info:
                    if (s.publicSelectionActive)
                    {
                        if (s.publicSelectionPlayerId == controller.LocalPlayerId && !controller.Store.HasPending(CommandTypes.RevealInformationWindow))
                        {
                            var choice = strategy.NextPublicChoice(view, s);
                            if (choice != null) controller.SelectInformationWindow(choice.CompanyId, choice.StartDay);
                        }
                        break;
                    }
                    if (controller.Store.HasPending(CommandTypes.BuyInformation) || controller.Store.HasPending(CommandTypes.BuyInformationWindow) || controller.IsDoneForCurrentPhase) break;
                    if (purchases < strategy.Policy.PurchaseBudget && failures < 3 &&
                        (!PhaseClock.HasDeadline(s) || clock.RemainingSeconds(s, now) > 2))
                    {
                        var buy = s.stockSystem ? strategy.NextRandomPurchase(view, s.lobbySettings) : strategy.NextPurchase(view, attempted);
                        if (buy != null)
                        {
                            attempted.Add(buy.CompanyId + ":" + (s.windowInformation ? buy.StartDay : buy.BundleIndex)); purchases++;
                            if (s.stockSystem) controller.BuyRandomInformation(buy.CompanyId);
                            else if (s.windowInformation) controller.SelectInformationWindow(buy.CompanyId, buy.StartDay, buy.EndDay);
                            else controller.BuyInformation(buy.CompanyId, buy.BundleIndex);
                            break;
                        }
                    }
                    if (controller.CanFinishPhase()) controller.FinishPhase();
                    break;
                case Protocol.Reveal:
                case Protocol.Result:
                    if (controller.CanFinishPhase()) controller.FinishPhase();
                    break;
                case Protocol.Selection:
                    if (controller.IsMyTurnToSelect() && !controller.Store.HasPending(CommandTypes.SelectCompany))
                    {
                        string pick = strategy.SelectCompany(view, s);
                        if (pick != null && controller.CanSelect(pick)) controller.SelectCompany(pick);
                        since = now;
                    }
                    break;
                case Protocol.Investment:
                    if (!controller.Store.OrderLocked && !controller.Store.HasPending(CommandTypes.SubmitInvestment) && orderAttempts < 2)
                    {
                        var order = strategy.BuildOrder(view, orderAttempts > 0);
                        if (order != null) { orderAttempts++; controller.SubmitInvestment(order); }
                        since = now;
                    }
                    break;
            }
        }
    }
}
