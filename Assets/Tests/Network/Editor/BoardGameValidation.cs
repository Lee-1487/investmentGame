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
            SegmentsAndSettlement(); passed.Add("BOARD 120/10/2 second timing, six segments, stale requests, settlement and rotation");
            Reactions(); passed.Add("BOARD FOOD cap, AUTO independence, AI accumulation/forced decline/reset, BIO one-shot probabilities across 300 seeds");
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
            Check(h.View(1).assets == StockRules.InitialAssets && h.View(1).assets == 100000000, "one hundred million starting assets");
            var s = h.Room.Snapshot();
            Check(s.phase == Protocol.Info && s.phaseEndsAtServerSeconds == 120, "120 seconds and no augment stage");
            Check(s.publicInformation.Length == 2 && s.publicInformation.All(c => c.startDay == 1 && c.days.SequenceEqual(new[] { 1, 2, 3, 4, 5 })), "first FOOD/AUTO cards public");
            Check(h.View(1).companies.Single(c => c.companyId == "FOOD").visibleWindows.Select(w => w.startDay).OrderBy(d => d).SequenceEqual(new[] { 1, 6, 26 }), "P1 FOOD segments 2 and 6");
            Check(h.View(2).companies.Single(c => c.companyId == "AUTO").visibleWindows.Select(w => w.startDay).OrderBy(d => d).SequenceEqual(new[] { 1, 6, 26 }), "P2 AUTO segments 2 and 6");
            Check(Players.All(p => h.View(int.Parse(p.Substring(1))).companies.Where(c => c.companyId == "AI" || c.companyId == "BIO").All(c => c.visibleWindows.Length == 0)), "no reactive future cards");
            Check(h.Send(1, "BUY_INFORMATION_WINDOW", new CommandPayload { companyId = "FOOD" }).errorCode == "INFORMATION_IS_DEALT", "no random purchases");
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
            var rejectReq = h.Send(1, StockRules.ShareInformation, new CommandPayload { targetPlayerId = "P3", companyId = "FOOD", startDay = 26, requestedCompanyId = "FOOD", requestedStartDay = 16 });
            Check(rejectReq.success && h.Send(3, StockRules.RespondExchange, new CommandPayload { exchangeId = rejectReq.payload.exchangeId, acceptExchange = false }).success, "reject exchange");
            Check(h.View(3).companies.Single(c => c.companyId == "FOOD").visibleWindows.Count(w => w.startDay == 26) == 0, "rejected exchange transfers nothing");
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
            var copy = DtoCopy.Copy(h.View(2)); copy.receivedTradeIntents[0].entryDay = 26;
            Check(h.View(2).receivedTradeIntents.Single().entryDay == 6, "intent defensive copy");
            h.FinishInfo(); h.Room.DrainMessages();
            Check(h.Position(1, "AI", 3).success && !h.Position(1, "AUTO", 1).success, "three tokens total");
            Check(h.Room.Snapshot().shareMarket.All(c => c.owners.All(o => o.shares == 0)), "pending placement absent from public state");
            Check(h.Room.DrainMessages().Where(d => d.Message.type == "PLAYER_VIEW").All(d => d.PlayerId == "P1"), "only actor receives private placement update");
            Check(h.Market.GetPortfolio("P1").usedSlots == 3 && h.Market.ProcessedDay == 0, "decision cannot advance market");
            Check(!h.Send(1, StockRules.SendPlan, new CommandPayload { targetPlayerId = "P2", companyId = "AI", startDay = 1, endDay = 5 }).success, "social closes with info phase");
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
                h.Deadline();
                Check(h.Room.Snapshot().stockSegmentRevealing && h.Market.ProcessedDay == segment * 5, "reveal begins at decision deadline");
                Check(h.Room.Snapshot().shareMarket.Single(c => c.companyId == "AUTO").owners.Single(o => o.playerId == "P1").shares == 3, "placement visible on reveal");
                Check(!h.Position(1, "AUTO", 0).success, "placement locked while revealing");
                h.Time.Seconds += 1; h.Room.Tick(); Check(h.Market.ProcessedDay == segment * 5 + 2, "two closes at reveal midpoint");
                h.Deadline(); Check(h.Market.ProcessedDay == (segment + 1) * 5, "five closes completed in two seconds");
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
            var food = Market(44); var baseline = food.GetWindow(1, "FOOD", 0, 31); var auto = food.GetWindow(1, "AUTO", 0, 31);
            for (int p = 1; p <= 4; p++) Place(food, p, "FOOD", 3);
            Reveal(food);
            var actual = food.GetWindow(1, "FOOD", 0, 6);
            for (int day = 1; day <= 5; day++) { int move = baseline[day] - baseline[day - 1]; Check(actual[day] - actual[day - 1] == move + Math.Sign(move) * 2000, "FOOD +20 point cap"); }
            Check(food.GetWindow(1, "AUTO", 0, 31).SequenceEqual(auto), "AUTO unchanged");
            var twoInvestors = Market(55); Place(twoInvestors, 1, "AI", 3); Place(twoInvestors, 2, "AI", 3); Reveal(twoInvestors);
            Check(twoInvestors.GetWindow(1, "AI", 5, 1)[0] == 1500 && Chance(twoInvestors, "AI") == 3000, "two players with six tokens do not force a fall; next risk is 30 percent");
            var threeInvestors = Market(55);
            for (int p = 1; p <= 3; p++) Place(threeInvestors, p, "AI", 1);
            Reveal(threeInvestors);
            Check(threeInvestors.GetWindow(1, "AI", 5, 1)[0] == -3000 && Chance(threeInvestors, "AI") == 0, "three players with three tokens force a fall and reset");
            int rises = 0, falls = 0;
            for (int seed = 0; seed < 100; seed++) {
                var ai = Market(seed); Place(ai, 1, "AI", 3); Reveal(ai);
                Check(ai.GetWindow(1, "AI", 5, 1)[0] == 1500 && Chance(ai, "AI") == 1500, "three tokens from one player add only 15 percent risk");
                Place(ai, 2, "AI", 1); Reveal(ai);
                int move = ai.GetWindow(1, "AI", 10, 1)[0] - ai.GetWindow(1, "AI", 5, 1)[0];
                Check(move == 1500 || move == -3000, "AI keeps segment rise and fall sizes");
                if (move == 1500) { rises++; Check(Chance(ai, "AI") == 3500, "consecutive rise adds four tokens times five percent to prior 15 percent"); }
                else { falls++; Check(Chance(ai, "AI") == 0, "random fall resets accumulated risk"); }
                Place(ai, 1, "AI", 0); Place(ai, 2, "AI", 0); Reveal(ai);
                Check(Chance(ai, "AI") == 0 && ai.GetWindow(1, "AI", 15, 1)[0] - ai.GetWindow(1, "AI", 10, 1)[0] == 1500, "empty segment resets risk before drawing, even after a rising streak");
                Place(ai, 1, "AI", 1); Reveal(ai);
                Check(Chance(ai, "AI") == 500 && ai.GetWindow(1, "AI", 20, 1)[0] - ai.GetWindow(1, "AI", 15, 1)[0] == 1500, "investment after empty segment restarts at five percent");
            }
            Check(rises > 0 && falls > 0, "accumulated risk permits both rising and falling paths");
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
            Check(h.Market.GetWindow(1, "AI", 1, 5).SequenceEqual(prices) && Chance(h.Market, "AI") == 1500, "restore committed prices and probabilities");
            Check(h.Send(1, "DEV_PREVIOUS").success && h.Market.DecisionOpen && Chance(h.Market, "AI") == 0, "rewind decision");
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
            var portfolio = new PortfolioState { insights = new[] { new StockInsight { companyId = "AI", probabilityBps = 3000 }, new StockInsight { companyId = "BIO", probabilityBps = 1300 } } };
            Check(BoardChartAnnotations.ProbabilityLabel(state, portfolio, "AI").Contains("2구간 하락 30%"), "current AI probability and target segment");
            Check(BoardChartAnnotations.ProbabilityLabel(state, portfolio, "BIO").Contains("2구간 Lightening 13%"), "current BIO probability and target segment");
            state.stockSegmentRevealing = true;
            Check(BoardChartAnnotations.ProbabilityLabel(state, portfolio, "BIO").Contains("3구간"), "reveal labels already calculated next probability");
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
