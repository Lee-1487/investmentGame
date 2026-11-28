using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Flow;
using BlackWhale.Rules;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;
using BlackWhale.UI.Dev;
using BlackWhale.UI.State;
using BlackWhale.UI.Presentation;

namespace BlackWhale.Tests
{
    public static partial class BoardGameValidation
    {
        private static readonly string[] Players = { "P1", "P2", "P3", "P4" };
        private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("BOARD: " + message); }
        private sealed class Clock : IServerClock { public double Seconds { get; set; } }
        private sealed class Harness
        {
            public readonly Clock Time = new Clock();
            public readonly MarketService Market;
            public readonly SharedInformationService Information;
            public readonly GameRoom Room;
            private int request;
            public Harness(int seed = 731, TimerMode mode = TimerMode.Realtime)
            {
                Market = MarketService.CreateStockSystem(seed);
                Information = new SharedInformationService(Market, seed);
                Room = new GameRoom("BOARD", Market, Information, Time, mode, randomSeed: seed);
                for (int i = 1; i <= 4; i++) Room.Join("c" + i, "Player " + i);
                for (int i = 1; i <= 4; i++) Check(Send(i, "SET_READY", new CommandPayload { ready = true }).success, "ready");
                Check(Send(1, "START_GAME").success, "start");
            }
            public CommandResult Send(int player, string type, CommandPayload payload = null, string id = null) => Room.ProcessCommand("c" + player,
                new Command { type = type, turn = Room.Snapshot().turn, developerRevision = Room.Snapshot().developerRevision,
                    requestId = id ?? "b" + (++request), payload = payload });
            public CommandResult Position(int player, string company, int count) => Send(player, StockRules.Position,
                new CommandPayload { startDay = Room.Snapshot().stockSegmentIndex * 5 + 1, shareOrders = new[] { new ShareOrder { companyId = company, targetShares = count } } });
            public PlayerView View(int player) => Information.BuildPlayerView(new RequestContext("BOARD", Room.Snapshot().turn, Room.Snapshot().phase, "P" + player), Market.GetOwnInvestmentState("P" + player));
            public void Deadline() { Time.Seconds = Room.Snapshot().phaseEndsAtServerSeconds; Room.Tick(); }
            public void FinishInfo() { for (int i = 1; i <= 4; i++) Check(Send(i, "COMPLETE_PHASE").success, "finish info"); }
        }

        public static string[] Run()
        {
            var passed = new List<string>();
            InformationAndPrivacy(); passed.Add("BOARD initial card ownership, exchange restrictions, intents, privacy, defensive DTO copies");
            DirectionCardsAndAiBuys(); passed.Add("BOARD AI late entry/top-ups, direction-only cards, exchanges, partial price reveal");
            SegmentsAndSettlement(); passed.Add("BOARD 120/10/2 second timing, six segments, stale requests, settlement and rotation");
            Reactions(); passed.Add("BOARD five-day parity patterns, AI probability table/compounding/withdrawals, cumulative returns, BIO across 300 seeds");
            DeveloperRewind(); passed.Add("BOARD developer rewind restores prices, allocations and probability state");
            ReservationAndRumorOverlays(); passed.Add("BOARD saved daily ranges, rumor selection/visibility, reconnect restoration, fixed probabilities and 100 million starting assets");
            using (var batch = new BotExperimentBatch(new BotExperimentSettings { repetitions = 3 }, StockPatternLibrary.CreateDefault())) {
                int steps = 0;
                while (batch.Running && steps++ < 100000) batch.Step();
                Check(!batch.Running && batch.Results.runs.Count == 3, "three complete bot games");
                Check(batch.Results.runs.All(r => r.turns.Count == 6 && r.commandFailures == 0), "bots finish six turns without rejected commands");
                Check(batch.Results.runs.SelectMany(r => r.turns).All(t => t.selectedAugments.All(string.IsNullOrEmpty)), "augments disabled");
            }
            passed.Add("BOARD three four-bot games finish all six rounds without command errors or augments");
            BotPolicyAndChartBehavior();
            passed.Add("BOARD bot affinity, probabilistic skill/personality, and BIO fixed axis (-40% to +240%p)");
            return passed.ToArray();
        }

        private static void BotPolicyAndChartBehavior()
        {
            var bio = ChartVerticalRange.BioFixed;
            Check(bio.MinBps == -4000 && bio.MaxBps == 24000, "BIO fixed vertical axis -40%..+240%");
            Check(bio.AxisTicks(5).SequenceEqual(new[] { 0, 10000, 20000 }), "BIO fixed axis ticks");

            var clickSnap = new ChartRangeSelection { MinimumDay = 1, SnapSegmentOnClick = true };
            clickSnap.Begin(12, Array.Empty<Interval>());
            clickSnap.Finish(12);
            Check(clickSnap.HasSelection && clickSnap.Start == 11 && clickSnap.End == 15, "single click snaps to full 5-day segment");

            var bot = new BlackWhale.Bots.InformationBotStrategy(17, new BlackWhale.Bots.BotPolicy(BlackWhale.Bots.BotDifficulty.Hard, BlackWhale.Bots.BotPersonality.Strategic));
            double initial = bot.GetAffinity("P2");
            bot.RecordExchangeOutcome("P2", true);
            Check(bot.GetAffinity("P2") > initial, "exchange acceptance increases affinity");

            var view = new PlayerView {
                turn = 1, recipientPlayerId = "P1",
                companies = new[] {
                    new VisibleCompany { companyId = "AI", visibleWindows = new[] { new VisibleInformationWindow { startDay = 1, dailyChangesBps = new[] { 300, 600, 900, 1200, -1800 } } } },
                    new VisibleCompany { companyId = "FOOD", visibleWindows = new[] { new VisibleInformationWindow { startDay = 6, source = "DEALT", dailyChangesBps = new[] { 100, 200, 300, 400, 500 } } } }
                },
                receivedTradeIntents = new[] { new TradeIntent { senderId = "P3", targetPlayerId = "P1", companyId = "FOOD", entryDay = 1, exitDay = 5 } }
            };
            var state = new PublicState {
                turn = 1,
                stockSegmentHistory = new[] {
                    new StockSegmentRecord {
                        segmentIndex = 0,
                        companies = new[] {
                            new CompanyShareState { companyId = "FOOD", owners = new[] { new ShareOwner { playerId = "P3", shares = 0 } } },
                            new CompanyShareState { companyId = "AI", owners = new[] { new ShareOwner { playerId = "P1", shares = 1 }, new ShareOwner { playerId = "P4", shares = 3 } } }
                        }
                    }
                }
            };
            bot.RecordSegmentOutcome(view, state, 0);
            Check(bot.GetAffinity("P3") < initial, "acting opposite to rumor decreases affinity");
            Check(bot.GetAffinity("P4") < initial, "losing money on AI due to others decreases affinity");

            // High-affinity sender has higher acceptance rate than low-affinity sender
            var reqHigh = new InformationExchange { senderId = "P2", offeredCompanyId = "AUTO", offeredStartDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 6 };
            var reqLow = new InformationExchange { senderId = "P3", offeredCompanyId = "AUTO", offeredStartDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 6 };
            int highAccepts = 0, lowAccepts = 0;
            for (int i = 0; i < 200; i++) {
                if (bot.ShouldAcceptExchange(view, reqHigh)) highAccepts++;
                if (bot.ShouldAcceptExchange(view, reqLow)) lowAccepts++;
            }
            Check(highAccepts > lowAccepts, "higher affinity yields higher exchange acceptance probability");
        }

        private static void InformationAndPrivacy()
        {
            var h = new Harness();
            foreach (string company in new[] { "AUTO", "FOOD" })
                Check(h.Send(1, StockRules.SendPlan, new CommandPayload { targetPlayerId = "P2", companyId = company, startDay = 1, endDay = 5 }).errorCode == "RUMOR_COMPANY_NOT_ALLOWED", "server blocks rumors on information-card companies");
            Check(h.View(1).assets == StockRules.InitialAssets && h.View(1).assets == 100000000, "one hundred million starting assets");
            var s = h.Room.Snapshot();
            Check(s.phase == Protocol.Info && s.phaseEndsAtServerSeconds == 120, "120 seconds and no augment stage");
            Check(s.publicInformation.Length == 2 && s.publicInformation.All(c => c.startDay == 1 && c.days.SequenceEqual(new[] { 1, 2, 3, 4, 5 })), "first FOOD/AUTO cards public");
            Check(h.View(1).companies.Single(c => c.companyId == "FOOD").visibleWindows.Select(w => w.startDay).OrderBy(d => d).SequenceEqual(new[] { 1, 6 }), "P1 FOOD second segment; sixth stays hidden");
            Check(h.View(2).companies.Single(c => c.companyId == "AUTO").visibleWindows.Select(w => w.startDay).OrderBy(d => d).SequenceEqual(new[] { 1, 6 }), "P2 AUTO second segment; sixth stays hidden");
            Check(Players.All(p => h.View(int.Parse(p.Substring(1))).companies.Where(c => c.companyId == "AI" || c.companyId == "BIO").All(c => c.visibleWindows.Length == 0)), "no reactive future cards");
            Check(h.Send(1, "BUY_INFORMATION_WINDOW", new CommandPayload { companyId = "FOOD" }).errorCode == "INFORMATION_IS_DEALT", "no random purchases");
            foreach (int player in Enumerable.Range(1, 4))
                foreach (var company in h.View(player).companies.Where(c => c.companyId == "AUTO" || c.companyId == "FOOD")) {
                    Check(company.visibleWindows.All(w => w.startDay < 26), "no player receives hidden direction card");
                    Check(BoardChartAnnotations.HiddenFinalSegment(company), "sixth segment uses HIDDEN presentation");
                }
            Check(h.Room.Snapshot().segmentKnowledge.All(k => k.startDay < 26), "no sixth-segment card ownership advertised");
            Check(h.Send(1, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P2", companyId = "FOOD",
                startDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 26 }).errorCode == "INVALID_WINDOW", "cannot request HIDDEN through exchange");
            Check(h.Send(1, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P2", companyId = "FOOD",
                startDay = 26, requestedCompanyId = "FOOD", requestedStartDay = 11 }).errorCode == "INVALID_WINDOW", "cannot offer HIDDEN through exchange");
            var crossCompany = h.Send(1, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P2", companyId = "FOOD", startDay = 6, requestedCompanyId = "AUTO", requestedStartDay = 6 });
            Check(!crossCompany.success && crossCompany.errorCode == "EXCHANGE_SAME_COMPANY", "cross-company exchange rejected");
            var share = new CommandPayload { targetPlayerId = "P2", companyId = "FOOD", startDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 11 };
            var shareRes = h.Send(1, StockRules.ShareInformation, share, "share-once");
            Check(shareRes.success && shareRes.payload?.exchangeId != null, "request mutual exchange");
            Check(h.Send(1, StockRules.ShareInformation, share, "share-once").success, "idempotent retry");
            Check(h.View(2).companies.Single(c => c.companyId == "FOOD").visibleWindows.Count(w => w.startDay == 6) == 0, "pending exchange does not leak card before acceptance");
            Check(!h.Send(1, "COMPLETE_PHASE").success, "cannot finish info phase while exchange is pending");
            Check(h.Send(2, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P3", companyId = "FOOD", startDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 16 }).errorCode == "INFORMATION_NOT_KNOWN", "pending received card cannot be forwarded");
            Check(h.Send(2, StockRules.RespondExchange, new CommandPayload { exchangeId = shareRes.payload.exchangeId, acceptExchange = true }, "accept-once").success, "accept exchange");
            Check(h.View(2).companies.Single(c => c.companyId == "FOOD").visibleWindows.Count(w => w.startDay == 6) == 1, "recipient receives offered card");
            Check(h.View(1).companies.Single(c => c.companyId == "FOOD").visibleWindows.Count(w => w.startDay == 11) == 1, "sender receives requested card");
            var rejectReq = h.Send(1, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P3", companyId = "FOOD", startDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 16 });
            Check(rejectReq.success && h.Send(3, StockRules.RespondExchange, new CommandPayload { exchangeId = rejectReq.payload.exchangeId, acceptExchange = false }).success, "reject exchange");
            Check(h.View(3).companies.Single(c => c.companyId == "FOOD").visibleWindows.Count(w => w.startDay == 6) == 0, "rejected exchange transfers nothing");
            Check(!h.Send(1, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P4", companyId = "FOOD", startDay = 16, requestedCompanyId = "FOOD", requestedStartDay = 21 }).success, "cannot forge unknown card");
            var forward = h.Send(1, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P4", companyId = "FOOD", startDay = 11, requestedCompanyId = "FOOD", requestedStartDay = 21 });
            Check(!forward.success && forward.errorCode == "INFORMATION_NOT_KNOWN", "exchanged information cannot be offered again");
            Check(!h.View(4).companies.Single(c => c.companyId == "FOOD").visibleWindows.Any(w => w.startDay == 11), "blocked forwarding reveals nothing");
            var ownership = h.Room.Snapshot().segmentKnowledge;
            Check(ownership.Single(k => k.companyId == "FOOD" && k.startDay == 6).playerIds.SequenceEqual(new[] { "P1" }), "offered card still lists only original owner");
            Check(ownership.Single(k => k.companyId == "FOOD" && k.startDay == 11).playerIds.SequenceEqual(new[] { "P2" }), "requested card does not list players who learned it");
            Check(ownership.Single(k => k.companyId == "FOOD" && k.startDay == 1).playerIds.SequenceEqual(new[] { "ALL" }), "public segment remains public");
            var originalAgain = h.Send(1, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P4", companyId = "FOOD", startDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 21 });
            Check(originalAgain.success && h.Send(4, StockRules.RespondExchange, new CommandPayload { exchangeId = originalAgain.payload.exchangeId, acceptExchange = true }).success, "original owner can exchange initially dealt information again");
            Check(h.Send(1, StockRules.SendPlan, new CommandPayload { targetPlayerId = "P2", companyId = "BIO", startDay = 6, endDay = 15 }).success, "send intent without exchange");
            Check(h.View(2).receivedTradeIntents.Single().senderId == "P1" && h.View(3).receivedTradeIntents.Length == 0, "intent recipient privacy");
            var annotations = BoardChartAnnotations.RumorKnowledge(h.View(2), h.Room.Snapshot(), true);
            Check(annotations.Single(k => k.companyId == "BIO" && k.startDay == 6).playerIds.SequenceEqual(new[] { "P1" }), "rumor sender appears in entry segment");
            Check(annotations.Single(k => k.companyId == "BIO" && k.startDay == 11).playerIds.Length == 0, "sender appears only in entry segment");
            Check(BoardChartAnnotations.RumorKnowledge(h.View(3), h.Room.Snapshot(), true).Where(k => k.companyId == "BIO").All(k => k.playerIds.Length == 0), "rumor names remain recipient-private");
            Check(BoardChartAnnotations.RumorKnowledge(h.View(2), h.Room.Snapshot(), false).Where(k => k.companyId == "BIO").All(k => k.playerIds.Length == 0), "rumor toggle hides sender names");
            var copy = DtoCopy.Copy(h.View(2)); copy.receivedTradeIntents[0].entryDay = 26;
            Check(h.View(2).receivedTradeIntents.Single().entryDay == 6, "intent defensive copy");
            h.FinishInfo(); h.Room.DrainMessages();
            Check(h.Position(1, "AI", 3).success && !h.Position(1, "AUTO", 1).success, "three tokens total");
            Check(h.Room.Snapshot().shareMarket.All(c => c.owners.All(o => o.shares == 0)), "pending placement absent from public state");
            Check(h.Room.DrainMessages().Where(d => d.Message.type == "PLAYER_VIEW").All(d => d.PlayerId == "P1"), "only actor receives private placement update");
            Check(h.Market.GetPortfolio("P1").usedSlots == 3 && h.Market.ProcessedDay == 0, "decision cannot advance market");
            Check(!h.Send(1, StockRules.SendPlan, new CommandPayload { targetPlayerId = "P2", companyId = "AI", startDay = 1, endDay = 5 }).success, "social closes with info phase");
        }

        private static void DirectionCardsAndAiBuys()
        {
            var h = new Harness();
            foreach (var company in h.View(1).companies)
                foreach (var card in company.visibleWindows) {
                    Check(card.direction == 1 || card.direction == -1, "known cards expose direction");
                    Check(card.dailyChangesBps.Length == 0 && card.dailyOpenBps.Length == 0, "no absolute close or open leaks");
                    Check(DtoCopy.Copy(card).direction == card.direction, "direction survives DTO copies");
                }
            var request = h.Send(1, StockRules.ShareInformation, new CommandPayload {
                targetPlayerId = "P2", companyId = "FOOD", startDay = 6, requestedCompanyId = "FOOD", requestedStartDay = 11 });
            Check(request.success && h.Send(2, StockRules.RespondExchange, new CommandPayload { exchangeId = request.payload.exchangeId, acceptExchange = true }).success, "direction cards exchange normally");
            var food = h.View(1).companies.Single(c => c.companyId == "FOOD");
            var separated = new VisibleCompany { companyId = "FOOD", visibleWindows = food.visibleWindows.Where(w => w.startDay == 1 || w.startDay == 11).ToArray() };
            var bars = DailyBarModel.Build(separated);
            Check(bars.Skip(1).All(b => !b.Bps.HasValue && !b.OpenBps.HasValue), "first and third cards cannot reveal middle absolute prices");
            Check(bars[1].SegmentDirection != 0 && bars[11].SegmentDirection != 0 && bars.Skip(6).Take(5).All(b => b.SegmentDirection == 0), "middle direction remains unknown");
            Check(!DailyBarModel.Tooltip(bars[11]).Contains("%"), "card tooltip contains no numerical price");
            h.FinishInfo();
            Check(h.Position(1, "AI", 1).success, "AI initial buy");
            h.Deadline(); h.Deadline();
            Check(h.Market.ProcessedDay == 5 && !h.Room.Snapshot().stockSegmentRevealing, "next decision");
            var view = h.Room.DrainMessages().Where(d => d.PlayerId == "P1" && d.Message.type == "PLAYER_VIEW").Last().Message.playerView;
            bars = DailyBarModel.Build(view.companies.Single(c => c.companyId == "FOOD"));
            Check(bars.Skip(1).Take(5).All(b => b.Bps.HasValue && b.OpenBps.HasValue), "actual prices replace first card progressively");
            Check(bars.Skip(6).All(b => !b.Bps.HasValue), "future cards retain hidden prices");
            Check(view.portfolio.AvailableToBuy(h.Room.Snapshot().shareMarket.Single(c => c.companyId == "AI")) > 0, "AI BUY enabled after day five");
            Check(h.Position(1, "AI", 2).success && h.Position(2, "AI", 1).success, "AI later top-up and first entry accepted");
            Check(h.Position(1, "AI", 1).success && h.Market.GetPortfolio("P1").holdings.Single(v => v.companyId == "AI").canBuy, "partial withdrawal keeps BUY available");
            Check(h.Position(1, "AI", 2).success, "same-decision AI reinvestment allowed");
            h.Deadline(); h.Deadline();
            view = h.Room.DrainMessages().Where(d => d.PlayerId == "P1" && d.Message.type == "PLAYER_VIEW").Last().Message.playerView;
            bars = DailyBarModel.Build(view.companies.Single(c => c.companyId == "FOOD"));
            int aiEntry = h.Market.GetWindow(1, "AI", 5, 1)[0], aiExit = h.Market.GetWindow(1, "AI", 10, 1)[0];
            int laterReturn = h.Market.GetPortfolio("P2").holdings.Single(v => v.companyId == "AI").returnBps;
            Check(Math.Abs(laterReturn - 10000m * (aiExit - aiEntry) / (10000m + aiEntry)) <= 1, "later AI entry earns return from its entry price");
            Check(bars[6].OpenBps == bars[5].Bps && bars.Skip(1).Take(10).All(b => b.Bps.HasValue), "next segment joins previous public close");
            Check(bars[11].SegmentDirection != 0 && !bars[11].Bps.HasValue, "exchanged third card stays direction-only until reveal");
            var upPreview = DailyBarModel.DirectionPreview(1); var downPreview = DailyBarModel.DirectionPreview(-1);
            Check(upPreview.Length == 6 && downPreview.Length == 6, "five preview candles");
            Check(Enumerable.Range(1, 5).All(i => upPreview[i] > upPreview[i - 1] && downPreview[i] < downPreview[i - 1]), "preview draws known direction");
            Check(upPreview.All(v => v >= 0 && v <= 1) && downPreview.All(v => v >= 0 && v <= 1), "preview uses independent normalized scale");
            Check(upPreview.Zip(downPreview, (u, d) => Math.Abs(u + d - 1f)).All(error => error < .0001f), "both directions use the same isolated scale");
            var finalPrices = h.Market.GetWindow(1, "FOOD", 0, 31);
            bars = DailyBarModel.Build(separated, new RevealedCompany { companyId = "FOOD", dailyChangesBps = finalPrices });
            Check(bars.Select(b => b.Bps.Value).SequenceEqual(finalPrices), "full reveal uses actual prices");
        }

        private static void SegmentsAndSettlement()
        {
            var h = new Harness(); h.FinishInfo();
            int[] auto = h.Market.GetWindow(1, "AUTO", 0, 31);
            Check(h.Position(1, "AUTO", 3).success, "all tokens AUTO");
            for (int segment = 0; segment < 6; segment++) {
                Check(h.Room.Snapshot().stockSegmentIndex == segment && !h.Room.Snapshot().stockSegmentRevealing, "decision starts");
                double end = h.Room.Snapshot().phaseEndsAtServerSeconds;
                h.Time.Seconds = end - .1; h.Room.Tick(); Check(h.Market.ProcessedDay == segment * 5, "no close before decision deadline");
                foreach (string company in new[] { "AUTO", "FOOD" }) {
                    var before = h.Room.DrainMessages().Where(d => d.PlayerId == "P1" && d.Message.type == "PLAYER_VIEW").Select(d => d.Message.playerView).LastOrDefault();
                    if (before != null && segment < 5)
                        Check(BoardChartAnnotations.HiddenFinalSegment(before.companies.Single(c => c.companyId == company)), "HIDDEN remains before final reveal");
                }
                h.Deadline();
                Check(h.Room.Snapshot().stockSegmentRevealing && h.Market.ProcessedDay == segment * 5, "reveal begins at decision deadline");
                Check(h.Room.Snapshot().shareMarket.Single(c => c.companyId == "AUTO").owners.Single(o => o.playerId == "P1").shares == 3, "placement visible on reveal");
                Check(!h.Position(1, "AUTO", 0).success, "placement locked while revealing");
                h.Time.Seconds += 1; h.Room.Tick(); Check(h.Market.ProcessedDay == segment * 5 + 2, "two closes at reveal midpoint");
                h.Deadline(); Check(h.Market.ProcessedDay == (segment + 1) * 5, "five closes completed in two seconds");
                var latest = h.Room.DrainMessages().Where(d => d.PlayerId == "P1" && d.Message.type == "PLAYER_VIEW").Select(d => d.Message.playerView).Last();
                foreach (string company in new[] { "AUTO", "FOOD" })
                    Check(BoardChartAnnotations.HiddenFinalSegment(latest.companies.Single(c => c.companyId == company)) == (segment < 5), "HIDDEN opens only during final segment");

                if (segment < 5) Check(h.Send(1, StockRules.Position, new CommandPayload { startDay = segment * 5 + 1,
                    shareOrders = new[] { new ShareOrder { companyId = "AUTO", targetShares = 0 } } }).errorCode == "STALE_SEGMENT", "reject delayed previous-segment order");
            }
            Check(h.Room.Snapshot().phase == Protocol.Reveal && h.Room.Snapshot().stockSegmentHistory.Length == 6, "full reveal only after six segments");
            var snapshot = DtoCopy.Copy(h.Room.Snapshot()); snapshot.stockSegmentHistory[0].companies[0].owners[0].shares = 99;
            Check(h.Room.Snapshot().stockSegmentHistory[0].companies[0].owners[0].shares != 99, "history defensive copy");
            h.Deadline();
            var board = h.Room.DrainMessages().Where(d => d.Message.type == "SCOREBOARD").Select(d => d.Message.scoreboard).First();
            Check(board.rows.Single(r => r.playerId == "P1").totalAssets == Math.Max(0, StockRules.InitialAssets + StockRules.InitialAssets * auto[30] / 10000), "fixed-capital additive token settlement");
            h.Deadline();
            Check(h.Room.Snapshot().turn == 2 && h.Room.Snapshot().phase == Protocol.Info, "next round skips augments");
            Check(h.View(4).companies.Single(c => c.companyId == "FOOD").visibleWindows.Any(w => w.startDay == 6 && w.source == "DEALT"), "P1..P4 roles rotate to B/C/D/A next round");
            Check(h.Market.GetPortfolio("P1").usedSlots == 0 && h.Market.GetPortfolio("P1").insights.Single(i => i.companyId == "BIO").probabilityBps == 500, "round resets tokens and BIO");
        }

        private static MarketService Market(int seed)
        {
            var m = MarketService.CreateStockSystem(seed); m.BeginTurn(new RequestContext("R", 1, Protocol.Info), Players); m.BeginLiveTrading(); return m;
        }
        private static void Place(MarketService m, int player, string company, int count) => Check(m.SetPosition(new RequestContext("R", 1, Protocol.Investment, "P" + player),
            new ShareOrder { companyId = company, targetShares = count }).Success, "set service position");
        private static void Reveal(MarketService m) { m.RevealSegment(); for (int i = 0; i < 5; i++) m.SimulateDay(); m.BeginDecision(); }
        private static int Chance(MarketService m, string company) => m.GetPortfolio("P1").insights.Single(i => i.companyId == company).probabilityBps;

        private static void Reactions()
        {
            Check(InvestmentRules.CompanyIds.SequenceEqual(new[] { "AUTO", "FOOD", "AI", "BIO" })
                && UiRules.CompanyIds.SequenceEqual(InvestmentRules.CompanyIds), "company order consistent");
            int[] up = { 5, 8, 12, 16, 22, 30 }, down = { 8, 12, 18, 25, 35, 45 };
            int[] deltas = { 500, -1000, 500, 1000, 1500 };
            for (int chance = 500; chance <= 9000; chance += 100)
                for (int investors = 0; investors <= 4; investors++) {
                    Check(MarketService.AccumulateAiUpChance(chance, investors, false) == Math.Max(500, Math.Min(9000, chance + deltas[investors])), "participant-only probability accumulation and limits");
                    Check(MarketService.AccumulateAiUpChance(chance, investors, true) == Math.Max(500, Math.Min(9000, chance + deltas[investors] + 1000)), "fall bonus accumulation and limits");
                }
            for (int streak = 0; streak < 6; streak++) for (int tokens = 1; tokens <= 12; tokens++) {
                Check(MarketService.AiSegmentReturnBps(streak, tokens, true) == up[streak] * 100 * (10 + (tokens - 1) * 2) / 10, "AI upside scales with tokens and streak");
                Check(MarketService.AiSegmentReturnBps(streak, tokens, false) == -Math.Min(9500, down[streak] * 100 * (10 + (tokens - 1) * 2) / 10), "AI downside scales with tokens and streak");
            }
            Check(MarketService.AccumulateAiUpChance(5000, 0) == 5500 && MarketService.AccumulateAiUpChance(9000, 0) == 9000, "empty AI gains five points up to cap");
            int rises = 0, falls = 0, consecutiveFalls = 0;
            for (int seed = 0; seed < 300; seed++) {
                var ai = Market(seed);
                foreach (string company in new[] { "AUTO", "FOOD" }) {
                    var prices = ai.GetWindow(1, company, 0, 31);
                    int rising = 0;
                    for (int segment = 0; segment < 6; segment++) {
                        int start = segment * 5, sign = Math.Sign(prices[start + 5] - prices[start]);
                        Check(sign != 0 && Enumerable.Range(1, 5).All(d => Math.Sign(prices[start + d] - prices[start + d - 1]) == sign), "one direction per five-day pattern");
                        if (segment == 5) {
                            Check(sign == (rising % 2 == 0 ? -1 : 1), "HIDDEN direction follows preceding five segments");
                            if (rising == 4) Check(sign == -1, "four rises and one fall finish with a fall");
                            if (rising == 3) Check(sign == 1, "three rises and two falls finish with a rise");
                        }
                        if (sign > 0) rising++;
                    }
                    Check(rising % 2 == 0 && (6 - rising) % 2 == 0, "hidden completes even counts");
                }
                for (int player = 1; player <= 4; player++) Place(ai, player, "AI", 1);
                int streakCount = 0, previous = 0, cumulativeUp = 5000; bool previousFall = false;
                for (int segment = 0; segment < 6; segment++) {
                    if (segment == 1) Place(ai, 4, "AI", 0);
                    int investors = segment == 0 ? 4 : 3;
                    var random = new Random(unchecked(seed * 397 ^ 7919 ^ segment * 104729 ^ 11));
                    bool rising = random.Next(10000) < cumulativeUp;
                    if (!rising && previousFall) consecutiveFalls++;
                    previousFall = !rising;
                    int target = (int)Math.Round((10000m + previous) * (1m + ((rising ? up[streakCount] : -down[streakCount]) * (10 + (investors - 1) * 2) / 10m) / 100m), MidpointRounding.AwayFromZero) - 10000;
                    var before = ai.GetPortfolio("P1").insights.Single(i => i.companyId == "AI");
                    Check(before.upProbabilityBps == cumulativeUp && before.probabilitySegment == segment, "next segment uses previously accumulated probability");
                    ai.RevealSegment();
                    Check(ai.GetPortfolio("P1").insights.Single(i => i.companyId == "AI").upProbabilityBps == cumulativeUp, "no probability update before five closes");
                    for (int day = 0; day < 5; day++) ai.SimulateDay();
                    ai.BeginDecision();
                    cumulativeUp = Math.Max(500, Math.Min(9000, cumulativeUp + deltas[investors] + (rising ? 0 : 1000)));
                    Check(ai.GetWindow(1, "AI", segment * 5 + 5, 1)[0] == target, "AI compounded endpoint, streak and withdrawal shock");
                    var portfolio = ai.GetPortfolio("P1");
                    Check(portfolio.holdings.Single(h => h.companyId == "AI").returnBps == target, "held AI return compounds like chart");
                    Check(Math.Abs(portfolio.totalReturnBps - target / 3m) <= 1, "overall return weighted by one of three slots");
                    Check(portfolio.insights.Single(i => i.companyId == "AI").upProbabilityBps == cumulativeUp, "displayed AI probability accumulates for next draw");
                    Check(portfolio.insights.Single(i => i.companyId == "AI").upProbabilityBps + portfolio.insights.Single(i => i.companyId == "AI").downProbabilityBps == 10000, "AI probabilities sum to 100 percent");
                    var copy = DtoCopy.Copy(portfolio);
                    Check(copy.totalReturnBps == portfolio.totalReturnBps && copy.holdings.Single(h => h.companyId == "AI").marketReturnBps == target, "return fields copied");
                    previous = target; streakCount = rising ? streakCount + 1 : 0;
                    if (rising) rises++; else falls++;
                }
            }
            Check(rises > 0 && falls > 0 && consecutiveFalls > 0, "AI permits repeated and consecutive falls");
            var oneToken = Market(27); var threeTokens = Market(27);
            Place(oneToken, 1, "AI", 1); Place(threeTokens, 1, "AI", 3);
            for (int segment = 0; segment < 6; segment++) {
                Reveal(oneToken); Reveal(threeTokens);
                if (segment == 0) {
                    int small = oneToken.GetWindow(1, "AI", 5, 1)[0], large = threeTokens.GetWindow(1, "AI", 5, 1)[0];
                    Check(large == small * 14 / 10, "three tokens increase the first segment move to 1.4 times");
                }
                Check(Chance(oneToken, "AI") == Chance(threeTokens, "AI"), "token count does not affect accumulated probability");
            }
            var withdrawn = Market(44); Place(withdrawn, 1, "AI", 3); Reveal(withdrawn);
            int locked = withdrawn.GetPortfolio("P1").totalReturnBps;
            int beforeEmptyChance = Chance(withdrawn, "AI");
            Place(withdrawn, 1, "AI", 0); Reveal(withdrawn);
            Check(Chance(withdrawn, "AI") == Math.Max(1000, beforeEmptyChance - 500), "uninvested AI still accumulates rise probability");
            Check(withdrawn.GetPortfolio("P1").totalReturnBps == locked, "withdrawal preserves realized profit");
            Check(withdrawn.GetWindow(1, "AI", 5, 6).Distinct().Count() == 1, "uninvested AI stays flat");
            Place(withdrawn, 1, "AI", 3);
            Check(withdrawn.GetPortfolio("P1").holdings.Single(h => h.companyId == "AI").canBuy, "BUY available after full withdrawal");
            int entryPrice = withdrawn.GetWindow(1, "AI", 10, 1)[0];
            Reveal(withdrawn);
            int exitPrice = withdrawn.GetWindow(1, "AI", 15, 1)[0];
            decimal reinvestment = 10000m * (exitPrice - entryPrice) / (10000m + entryPrice);
            Check(Math.Abs(withdrawn.GetPortfolio("P1").totalReturnBps - locked - reinvestment) <= 1, "reinvestment adds entry-relative profit while preserving realized gain");
            var mixed = Market(19); Place(mixed, 1, "AUTO", 1); Place(mixed, 1, "FOOD", 2); Reveal(mixed);
            var combined = mixed.GetPortfolio("P1");
            Check(Math.Abs(combined.totalReturnBps - combined.holdings.Sum(h => h.returnBps * h.shares) / 3m) <= 1, "mixed company returns weighted by investment");
            mixed.LockPortfolios();
            int preview = mixed.GetPortfolio("P1").totalReturnBps;
            var final = mixed.SettleTurn(new RequestContext("R", 1, Protocol.Result));
            Check(final.rows.Single(r => r.playerId == "P1").turnReturnBps == preview, "live cumulative return matches settlement");
            int occurredGames = 0, missedGames = 0;
            for (int seed = 0; seed < 300; seed++) {
                var bio = Market(seed); bool occurred = false; int expectation = 0, events = 0;
                for (int segment = 0; segment < 6; segment++) {
                    int count = new[] { 1, 2, 0, 3, 3, 3 }[segment];
                    Place(bio, 1, "BIO", count); int start = segment * 5;
                    Check(Chance(bio, "BIO") == (occurred ? 0 : 500 + expectation), "BIO next chance follows history");
                    Reveal(bio);
                    var values = bio.GetWindow(1, "BIO", start, 6);
                    int spikes = Enumerable.Range(1, 5).Count(d => values[d] - values[d - 1] == 20000);
                    Check(Enumerable.Range(1, 5).All(d => values[d] - values[d - 1] == 20000 || Math.Abs(values[d] - values[d - 1]) == 100), "BIO quiet days are one point");
                    events += spikes; occurred |= spikes > 0;
                    expectation = occurred ? 0 : count == 0 ? expectation / 2 : Math.Min(3500, expectation + (count == 1 ? 500 : count == 2 ? 800 : 1000));
                    Check(Chance(bio, "BIO") == (occurred ? 0 : 500 + expectation) && Chance(bio, "BIO") <= 4000, "BIO cap/halving/zero after event");
                    var bioInsight = bio.GetPortfolio("P1").insights.Single(i => i.companyId == "BIO");
                    Check(bioInsight.upProbabilityBps == Chance(bio, "BIO") && bioInsight.downProbabilityBps == 10000 - Chance(bio, "BIO")
                        && bioInsight.probabilitySegment == segment + 1, "BIO display reflects next accumulated event probability");
                }
                Check(events <= 1, "at most one lightning in thirty days");
                if (occurred) occurredGames++; else missedGames++;
            }
            Check(occurredGames > 0 && missedGames > 0, "event is possible and not guaranteed");
        }

        private static void DeveloperRewind()
        {
            var h = new Harness(64, TimerMode.Developer); h.FinishInfo();
            Check(h.Position(1, "AI", 3).success, "developer position");
            Check(h.Send(1, "DEV_NEXT").success && h.Room.Snapshot().stockSegmentRevealing, "developer starts reveal");
            int[] prices = h.Market.GetWindow(1, "AI", 1, 5);
            Check(h.Send(1, "DEV_NEXT").success && h.Market.ProcessedDay == 5, "developer next decision");
            Check(h.Send(1, "DEV_PREVIOUS").success && h.Market.ProcessedDay == 0 && h.Room.Snapshot().stockSegmentRevealing, "rewind reveal");
            Check(h.Market.GetWindow(1, "AI", 1, 5).SequenceEqual(prices) && Chance(h.Market, "AI") == 5000, "restore committed prices and probabilities");
            Check(h.Send(1, "DEV_PREVIOUS").success && h.Market.DecisionOpen && Chance(h.Market, "AI") == 5000, "rewind decision");
            Check(h.Room.Snapshot().stockSegmentHistory.Length == 0 && h.Market.GetWindow(1, "AI", 1, 5).All(v => v == 0), "clear future reveal and prices");
        }

        private sealed class PlanningClient : IGameClient
        {
            public event Action<ServerMessage> MessageReceived;
            public readonly List<Command> Commands = new List<Command>();
            public void SendCommand(Command command) => Commands.Add(command);
            public void Deliver(ServerMessage message) => MessageReceived?.Invoke(message);
            public void Pump() { }
            public void Dispose() { }
        }

        private static void ReservationAndRumorOverlays()
        {
            var h = new Harness();
            var sender = new PlanningClient();
            using (var controller = new GameClientController(sender)) {
                sender.Deliver(new ServerMessage { type = "PUBLIC_STATE", state = h.Room.Snapshot() });
                var view = h.View(1); view.portfolio = h.Market.GetPortfolio("P1");
                view.reservedInvestments = h.Market.GetReservations("P1");
                sender.Deliver(new ServerMessage { type = "PLAYER_VIEW", playerView = view });
                Check(controller.CanPlanTrade("BIO"), "info phase permits reservations on reactive companies");
                Check(controller.Store.TradePlans.TryAddInterval("BIO", 3, 9) == "", "arbitrary daily reservation");
                Check(controller.Store.TradePlans.TryAddInterval("BIO", 17, 22) == "", "second reserved interval");
                Check(sender.Commands.All(c => c.type == "SAVE_TRADE_PLAN"), "planning never places investment tokens");
                foreach (var command in sender.Commands) Check(h.Room.ProcessCommand("c1", command).success, "save private reservations on server");
                var saved = h.Market.GetReservations("P1").Single(i => i.companyId == "BIO").intervals;
                Check(saved.Length == 2 && saved[0].entryDay == 3 && saved[1].exitDay == 22, "server stores exact daily ranges");
                Check(controller.SendTradeIntent("P2", "BIO", saved[1].entryDay, saved[1].exitDay) == CommandStatus.Sent, "select saved interval for rumor");
                Check(h.Room.ProcessCommand("c1", sender.Commands.Last()).success, "send saved interval beyond five-day boundaries");
                Check(h.View(2).receivedTradeIntents.Single().entryDay == 17 && h.View(2).receivedTradeIntents.Single().exitDay == 22, "recipient gets selected range only");
                Check(h.View(3).receivedTradeIntents.Length == 0, "third party cannot see rumor");
                Check(h.Market.GetPortfolio("P1").usedSlots == 0, "reservation/rumor cannot automatically invest");
                Check(h.Send(2, StockRules.SendPlan, new CommandPayload { targetPlayerId = "P1", companyId = "BIO", startDay = 7, endDay = 12 }).success, "receive another player's rumor");
                var restored = h.View(1); restored.portfolio = h.Market.GetPortfolio("P1"); restored.reservedInvestments = h.Market.GetReservations("P1");
                sender.Deliver(new ServerMessage { type = "PLAYER_VIEW", playerView = restored });
                var notes = BoardChartAnnotations.Build(controller.Store.TradePlans, controller.Store.View, "BIO", "P1", controller.Store.RumorsVisible);
                Check(notes.Count(n => !n.rumor && n.playerId == "P1") == 2 && notes.Single(n => n.rumor).playerId == "P2", "own and received plans retain player colors/identities");
                Check(BoardChartAnnotations.DayNotes(notes, 8).Contains("P2 찌라시 7~12일"), "hover identifies rumor sender and exact dates");
                controller.Store.ToggleRumors();
                var hidden = BoardChartAnnotations.Build(controller.Store.TradePlans, controller.Store.View, "BIO", "P1", controller.Store.RumorsVisible);
                Check(hidden.Length == 2 && hidden.All(n => !n.rumor), "hide rumor without hiding own reservations");
                Check(!BoardChartAnnotations.DayNotes(hidden, 8).Contains("P2"), "hidden rumor is removed from hover text too");
                Check(controller.Store.View.receivedTradeIntents.Length == 1, "hiding rumor preserves inbox");
                controller.Store.ToggleRumors();
                Check(BoardChartAnnotations.Build(controller.Store.TradePlans, controller.Store.View, "BIO", "P1", controller.Store.RumorsVisible).Length == 3, "show rumor again");
                h.FinishInfo();
                sender.Deliver(new ServerMessage { type = "PUBLIC_STATE", state = h.Room.Snapshot() });
                Check(!controller.CanPlanTrade("BIO") && controller.Store.TradePlans.Get("BIO").Intervals.Length == 2, "investment preserves frozen planning ranges");
                int ai = Chance(h.Market, "AI"), bio = Chance(h.Market, "BIO");
                Check(h.Position(1, "BIO", 1).success, "buy first token");
                Check(h.Position(1, "BIO", 2).success && h.Market.GetPortfolio("P1").usedSlots == 2, "buy one additional token");
                Check(h.Position(1, "BIO", 1).success && h.Market.GetPortfolio("P1").usedSlots == 1, "sell one token");
                Check(Chance(h.Market, "AI") == ai && Chance(h.Market, "BIO") == bio, "current placements do not change this segment's probability");
                var reconnect = new ClientStateStore(); reconnect.SetPublicState(h.Room.Snapshot()); reconnect.SetPlayerView(restored);
                Check(reconnect.TradePlans.Get("BIO").Intervals.Length == 2, "reconnected client restores exact reservations");
                Check(BoardChartAnnotations.Build(reconnect.TradePlans, reconnect.View, "BIO", "P1", true).Length == 3, "reconnect restores chart annotations");
            }
            var range = new ChartRangeSelection { MinimumDay = 1 }; range.SetRange(0, 0);
            Check(range.Start == 1 && range.End == 1, "board reservations start at day one");
            var state = new PublicState { phase = Protocol.Investment, stockSegmentIndex = 1 };
            var portfolio = new PortfolioState { insights = new[] { new StockInsight { companyId = "AI", upProbabilityBps = 7000, downProbabilityBps = 3000, probabilitySegment = 1 }, new StockInsight { companyId = "BIO", upProbabilityBps = 1300, downProbabilityBps = 8700, probabilitySegment = 1 } } };
            Check(BoardChartAnnotations.ProbabilityLabel(state, portfolio, "AI").Contains("상승 70%") && BoardChartAnnotations.ProbabilityLabel(state, portfolio, "AI").Contains("하락 30%"), "AI rise and fall highlighted");
            Check(BoardChartAnnotations.ProbabilityLabel(state, portfolio, "BIO").Contains("급등 13%") && BoardChartAnnotations.ProbabilityLabel(state, portfolio, "BIO").Contains("미발생 87%"), "BIO rise and fall highlighted");
            state.stockSegmentRevealing = true;
            Check(BoardChartAnnotations.ProbabilityLabel(state, portfolio, "BIO").Contains("다음 2구간"), "probability label identifies next segment");
        }
    }
}

#if UNITY_INCLUDE_TESTS
namespace BlackWhale.Tests
{
    public sealed class BoardGameEditorTests
    {
        [NUnit.Framework.Test] public void BoardGameRulesAndFlow() { BoardGameValidation.Run(); }
    }
}
#endif
