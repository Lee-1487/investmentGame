using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlackWhale.Flow;
using BlackWhale.Rules;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;
using BlackWhale.UI.State;

namespace BlackWhale.Tests
{
    public static class StockSystemValidation
    {
        private static void Check(bool ok, string message) => RoomValidation.Check(ok, "STOCK-SYSTEM: " + message);
        private static readonly string[] Players = { "P1", "P2", "P3", "P4" };
        private sealed class Harness
        {
            public readonly TestClock Clock = new TestClock();
            public readonly MarketService Market = MarketService.CreateStockSystem(7741);
            public readonly SharedInformationService Info;
            public readonly GameRoom Room;
            private int sequence;
            public Harness()
            {
                Info = new SharedInformationService(Market);
                Room = new GameRoom("STOCK", Market, Info, Clock, fixedOrderForFixture: true,
                    settings: new GameSettings(5, 6, 2, enableProgressionFeatures: false));
                for (int i = 1; i <= 4; i++) { Room.Join("c" + i, "Player " + i); Send(i, "SET_READY", new CommandPayload { ready = true }); }
                Send(1, "START_GAME");
            }
            public CommandResult Send(int player, string type, CommandPayload payload = null)
                => Room.ProcessCommand("c" + player, new Command { type = type, turn = Room.Snapshot().turn, requestId = "s" + (++sequence), payload = payload });
            public void FinishInfo()
            {
                while (Room.Snapshot().publicSelectionActive)
                {
                    var s = Room.Snapshot(); int p = int.Parse(s.publicSelectionPlayerId.Substring(1));
                    Check(Send(p, "REVEAL_INFORMATION_WINDOW", new CommandPayload { companyId = UiCompany(s.publicSelectionIndex), startDay = 1 }).success, "public pick");
                }
                for (int p = 1; p <= 4; p++) Check(Send(p, "COMPLETE_PHASE").success, "complete info");
            }
            private string UiCompany(int index) => new[] { "AI", "BANK", "FOOD", "AUTO" }[index];
            public void Choose()
            {
                foreach (var id in Players)
                {
                    var p = Market.GetPortfolio(id);
                    Check(Send(int.Parse(id.Substring(1)), StockRules.Choose, new CommandPayload { augmentId = p.candidates[0], companyId = "AI" }).success, "choose own candidate");
                }
            }
            public CommandResult Trade(int player, string company, int shares) => Send(player, StockRules.Trade,
                new CommandPayload { shareOrders = new[] { new ShareOrder { companyId = company, targetShares = shares } } });
            public void Deadline() { Clock.Seconds = Room.Snapshot().phase == Protocol.Selection ? Room.Snapshot().selectionDeadlineAtServerSeconds : Room.Snapshot().phaseEndsAtServerSeconds; Room.Tick(); }
        }
        public static string[] Run()
        {
            var names = new List<string>();
            Action<string, Action> run = (name, test) => { test(); names.Add("STOCK-SYSTEM/" + name); };
            run("twenty-shares-thirty-slots-public-ownership-and-atomic-draft", Draft);
            run("slot-weighted-multicompany-cash-and-settlement-replay", Settlement);
            run("augment-candidates-discounts-focus-flexibility-and-priority", Augments);
            run("six-turns-augmentation-schedule-carryover-timeouts-and-final-ranking", SixTurns);
            run("private-analysis-public-portfolio-copy-and-json-integers", Copies);
            run("all-augment-effect-boundaries-information-ledger-and-long-hold-bonuses", Effects);
            return names.ToArray();
        }
        private static void Draft()
        {
            var h = new Harness(); h.FinishInfo();
            Check(h.Room.Snapshot().phase == StockRules.AugmentPhase && h.Room.Snapshot().phaseEndsAtServerSeconds > 0, "timed augment selection after info"); h.Choose();
            Check(h.Room.Snapshot().draftOrder.SequenceEqual(new[] { "P1", "P2", "P3", "P4", "P4", "P1", "P2", "P3" }), "specified non-snake draft order");
            Check(h.Trade(2, "AI", 10).errorCode == "NOT_YOUR_TURN", "authenticated turn gate");
            Check(h.Trade(1, "AI", 11).errorCode == "SHARE_LIMIT", "individual limit checked");
            Check(h.Trade(1, "AI", 10).success && h.Trade(2, "AI", 10).success, "twenty shares can be split across two players");
            Check(h.Trade(3, "AI", 1).errorCode == "INSUFFICIENT_SHARES" && h.Room.Snapshot().selectionIndex == 2, "sold out request does not advance or change holdings");
            Check(h.Room.Snapshot().shareMarket.Single(c => c.companyId == "AI").owners.Sum(v => v.shares) == 20, "public ownership is exact");
            Check(h.Trade(3, "BANK", 8).success && h.Trade(4, "FOOD", 8).success && h.Trade(4, "AUTO", 7).success && h.Trade(1, "AUTO", 8).success && h.Trade(2, "SPACE", 6).success && h.Trade(3, "BIO", 5).success, "all eight picks commit");
            Check(h.Room.Snapshot().phase == Protocol.Reveal && h.Market.GetPortfolio("P1").usedSlots == 18, "cash remains and no fixed two-company order phase");
            var revealed = h.Market.GetRevealedMarket(new RequestContext("STOCK", 1, Protocol.Reveal));
            Check(revealed.Single(c => c.companyId == "AI").investments.Single(p => p.playerId == "P1").shares == 10, "public results preserve share counts");
        }
        private static void Settlement()
        {
            var market = MarketService.CreateStockSystem(19); market.BeginTurn(new RequestContext("S", 1, Protocol.Info), Players);
            var c = new RequestContext("S", 1, Protocol.Selection, "P1");
            foreach (var order in new[] { new ShareOrder { companyId = "AI", targetShares = 10 }, new ShareOrder { companyId = "FOOD", targetShares = 5 }, new ShareOrder { companyId = "BANK", targetShares = 3 } })
                Check(market.TradeShares(c, new[] { order }).Success, "three companies supported");
            Check(market.GetPortfolio("P1").usedSlots == 18, "twelve cash slots");
            market.LockPortfolios();
            long expected = 10000;
            foreach (var p in market.GetPortfolio("P1").holdings.Where(v => v.shares > 0)) expected += 10000L * p.shares / 30 * market.GetWindow(1, p.companyId, 30, 1)[0] / 10000;
            var board = market.SettleTurn(new RequestContext("S", 1, Protocol.Reveal));
            Check(board.rows.Single(p => p.playerId == "P1").totalAssets == expected, "allocation uses shares/30 without losing uninvested capital");
            Check(market.SettleTurn(new RequestContext("S", 1, Protocol.Reveal)).rows.Single(p => p.playerId == "P1").totalAssets == expected, "settlement is idempotent");
            market.BeginTurn(new RequestContext("S", 2, Protocol.Info), Players);
            Check(market.GetPortfolio("P1").usedSlots == 18 && market.GetPortfolio("P1").holdings.Count(v => v.shares > 0) == 3, "multicompany holdings carry to next turn");
            Check(market.TradeShares(new RequestContext("S", 2, Protocol.Selection, "P1"), new[] { new ShareOrder { companyId = "AI", targetShares = 0 } }).Success,
                "selling returns stock to supply");
            Check(market.GetShareMarket().Single(v => v.companyId == "AI").owners.Sum(v => v.shares) == 0, "sold supply available to another player");
        }
        private static List<string> OwnedAugments(MarketService market, string id)
        {
            var dictionary = (IDictionary)typeof(MarketService).GetField("stockPlayers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(market);
            var player = dictionary[id]; return (List<string>)player.GetType().GetField("Augments").GetValue(player);
        }
        private static void Augments()
        {
            var h = new Harness(); h.FinishInfo();
            foreach (var id in Players) Check(h.Market.GetPortfolio(id).candidates.Distinct().Count() == 3, "three distinct unowned candidates");
            Check(h.Send(1, StockRules.Choose, new CommandPayload { augmentId = "not-a-candidate" }).errorCode == "INVALID_AUGMENT", "cannot invent an augment"); h.Choose();
            OwnedAugments(h.Market, "P1").Add("FLEXIBLE"); OwnedAugments(h.Market, "P1").Add("PRIORITY");
            Check(h.Send(1, StockRules.Trade, new CommandPayload { shareOrders = new[] { new ShareOrder { companyId = "AI", targetShares = 10 }, new ShareOrder { companyId = "BANK", targetShares = 10 } } }).success,
                "flexible investor atomically adjusts two firms");
            Check(h.Send(1, StockRules.Priority).success, "future turn moves ahead without replacing active P2");
            Check(h.Room.Snapshot().currentPlayerId == "P2" && h.Room.Snapshot().draftOrder[4] == "P1", "priority preserves already completed and active picks");
            Check(h.Send(1, StockRules.Priority).errorCode == "PRIORITY_UNAVAILABLE", "priority usable once");
            var order = new Command { type = StockRules.Trade, requestId = "share-replay", turn = 1, payload = new CommandPayload { shareOrders = new[] { new ShareOrder { companyId = "FOOD", targetShares = 5 } } } };
            Check(h.Room.ProcessCommand("c2", order).success && h.Room.ProcessCommand("c2", order).success, "share request replay does not advance twice");
            order.payload.shareOrders[0].targetShares = 4; Check(h.Room.ProcessCommand("c2", order).errorCode == "REQUEST_ID_CONFLICT", "share quantities included in fingerprint");
        }
        private static void SixTurns()
        {
            var h = new Harness(); var augmentationTurns = new List<int>();
            while (h.Room.Snapshot().phase != Protocol.Ended)
            {
                var s = h.Room.Snapshot();
                if (s.phase == Protocol.Info) h.FinishInfo();
                else if (s.phase == StockRules.AugmentPhase) { augmentationTurns.Add(s.turn); h.Deadline(); }
                else if (s.phase == Protocol.Selection)
                {
                    if (s.turn == 1 && s.selectionIndex == 0) Check(h.Trade(1, "FOOD", 7).success, "first turn holding");
                    else h.Deadline();
                }
                else h.Deadline();
                Check(h.Room.Snapshot().turn <= 6, "never enters turn seven");
            }
            Check(augmentationTurns.SequenceEqual(new[] { 1, 3, 5 }), "three timed augmentation rounds");
            Check(h.Market.GetPortfolio("P1").holdings.Single(v => v.companyId == "FOOD").shares == 7, "timeout retains existing portfolio");
            Check(h.Market.GetPortfolio("P1").augments.Length == 3, "one new augment each round, timeout selects a valid first candidate");
            var final = h.Room.DrainMessages().Last(d => d.Message.type == "SCOREBOARD").Message.scoreboard;
            Check(final.turn == 6 && final.gameComplete && !string.IsNullOrEmpty(final.winnerPlayerId), "server declares winner at turn six");
        }
        private static void Copies()
        {
            var h = new Harness(); h.Room.DrainMessages();
            var s = h.Room.Snapshot(); var copy = DtoCopy.Copy(s); copy.shareMarket[0].owners[0].shares = 999;
            Check(h.Room.Snapshot().shareMarket[0].owners[0].shares == 0, "public share snapshots cannot mutate the ledger");
            var p = h.Market.GetPortfolio("P1"); p.holdings[0].shares = 123;
            Check(h.Market.GetPortfolio("P1").usedSlots == 0, "private portfolio copies isolated");
            bool rejected = false;
            try { BlackWhale.Network.RequestJsonValidator.Validate("{\"command\":{\"payload\":{\"shareOrders\":[{\"companyId\":\"AI\",\"targetShares\":1.5}]}}}"); } catch (FormatException) { rejected = true; }
            Check(rejected, "share count cannot be fractional or coerced by JSON decoding");
            Check(StockRules.Augments.Length == 12 && StockRules.Augments.All(id => StockRules.Description(id).Length > 0), "all twelve augments defined");
        }
        private static void Effects()
        {
            var m = MarketService.CreateStockSystem(19); m.BeginTurn(new RequestContext("E", 1, Protocol.Info), Players);
            m.BeginAugmentSelection();
            var all = (IDictionary)typeof(MarketService).GetField("stockPlayers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(m);
            var p1 = all["P1"]; p1.GetType().GetField("Candidates").SetValue(p1, new[] { "CONCENTRATION", "ANALYST", "TREND" });
            Check(m.ChooseAugment(new RequestContext("E", 1, StockRules.AugmentPhase, "P1"), "CONCENTRATION", "BIO").Success, "concentration chooses a real target firm");
            Func<string, string, int, RuleResult> trade = (p, c, q) => m.TradeShares(new RequestContext("E", 1, Protocol.Selection, p), new[] { new ShareOrder { companyId = c, targetShares = q } });
            Check(trade("P1", "BIO", 12).Success && trade("P1", "AI", 11).ErrorCode == "SHARE_LIMIT", "only focused firm has twelve-share limit");
            Check(trade("P1", "AI", 10).Success && trade("P1", "SPACE", 9).ErrorCode == "PORTFOLIO_FULL" && m.GetPortfolio("P1").usedSlots == 22, "thirty-slot cap remains atomic with concentration");
            Check(trade("P2", "BIO", 8).Success, "twelve plus eight still respects total supply");
            OwnedAugments(m, "P3").Add("OPPORTUNITY"); OwnedAugments(m, "P3").Add("ANALYST"); OwnedAugments(m, "P3").Add("BIO_RESEARCH");
            Check(trade("P3", "BIO", 1).ErrorCode == "INSUFFICIENT_SHARES" && trade("P3", "BIO", 2).ErrorCode == "INSUFFICIENT_SHARES" && m.InformationDiscount("P3") == 2,
                "opportunity awards two days only once, never fills unavailable stock");
            Check(m.GetPortfolio("P3").insights.Single().probabilityBps == MarketService.BioSuccessChance(19, 1) && m.GetPortfolio("P2").insights.Length == 0, "BIO chance is private and tied to its actual event generator");
            OwnedAugments(m, "P1").Add("TREND"); OwnedAugments(m, "P1").Add("CONTRARIAN");
            m.LockPortfolios(); m.SettleTurn(new RequestContext("E", 1, Protocol.Reveal));
            m.BeginTurn(new RequestContext("E", 2, Protocol.Info), Players);
            Check(m.InformationDiscount("P3") == 4, "analyst and one opportunity coupon stack to four days next info phase");
            var info = new SharedInformationService(m); info.GrantTurnCoins(new RequestContext("E", 2, Protocol.Info), Players);
            for (int i = 0; i < 4; i++) Check(info.RevealWindow(new RequestContext("E", 2, Protocol.Info, Players[i], "p" + i), InvestmentRules.CompanyIds[i], 10, false).Success, "public analysis setup");
            var own = info.BuildPlayerView(new RequestContext("E", 2, Protocol.Info, "P1"), m.GetOwnInvestmentState("P1"));
            var other = info.BuildPlayerView(new RequestContext("E", 2, Protocol.Info, "P2"), m.GetOwnInvestmentState("P2"));
            var analysis = m.AnalysisWindows("P1");
            Check(analysis.All(w => w.dailyChangesBps.Length == 3) && other.companies.All(c => c.visibleWindows.All(w => w.source != "AUGMENT")), "trend and contrarian reveal only three days to their owner");
            var buyContext = new RequestContext("E", 2, Protocol.Info, "P3", "discounted-buy");
            var bought = info.BuyWindow(buyContext, "BIO", 20, 24);
            Check(bought.Success && bought.Payload.paidCoins == 1 && m.InformationDiscount("P3") == 0, "information discount charges only one of five days");
            Check(info.BuyWindow(buyContext, "BIO", 20, 24).Payload.paidCoins == 1 && m.InformationDiscount("P3") == 0, "discount replay does not consume twice");
            if (analysis.Length > 0)
            {
                var w = analysis[0]; var purchased = info.BuyWindow(new RequestContext("E", 2, Protocol.Info, "P1", "analysis-mask"), w.companyId, 1, 5);
                Check(purchased.Success && purchased.Payload.paidCoins == 2, "analysis proof participates in the paid-information union");
            }
            var cash = MarketService.CreateStockSystem(1); cash.BeginTurn(new RequestContext("C", 1, Protocol.Info), Players); OwnedAugments(cash, "P1").Add("CASH_INTEREST"); cash.LockPortfolios();
            Check(cash.SettleTurn(new RequestContext("C", 1, Protocol.Reveal)).rows.Single(v => v.playerId == "P1").totalAssets == 10100, "cash bonus is capped at one percent of total assets");
            var holding = MarketService.CreateStockSystem(41);
            for (int t = 1; t <= 3; t++)
            {
                holding.BeginTurn(new RequestContext("H", t, Protocol.Info), Players);
                if (t == 1)
                {
                    OwnedAugments(holding, "P1").AddRange(new[] { "SPACE_PIONEER", "LONG_TERM" });
                    Check(holding.TradeShares(new RequestContext("H", t, Protocol.Selection, "P1"), new[] { new ShareOrder { companyId = "SPACE", targetShares = 10 } }).Success, "long hold setup");
                }
                int expectedRate = holding.GetWindow(t, "SPACE", 30, 1)[0] + (t >= 2 ? 200 : 0) + (t >= 3 ? 200 : 0);
                holding.LockPortfolios(); holding.SettleTurn(new RequestContext("H", t, Protocol.Reveal));
                Check(holding.GetSettlementDetail(t).Single(v => v.playerId == "P1").companyProfits.Single().returnBps == expectedRate, "two-turn SPACE and three-turn long-term bonuses apply at exact boundaries");
            }
            var diversified = MarketService.CreateStockSystem(42); diversified.BeginTurn(new RequestContext("D", 1, Protocol.Info), Players); OwnedAugments(diversified, "P1").Add("DIVERSIFY");
            var firms = InvestmentRules.CompanyIds.OrderBy(id => diversified.GetWindow(1, id, 30, 1)[0]).Take(3).ToArray();
            foreach (var id in firms) Check(diversified.TradeShares(new RequestContext("D", 1, Protocol.Selection, "P1"), new[] { new ShareOrder { companyId = id, targetShares = 10 } }).Success, "three meaningful holdings");
            diversified.LockPortfolios(); diversified.SettleTurn(new RequestContext("D", 1, Protocol.Reveal));
            foreach (var v in diversified.GetSettlementDetail(1).Single(p => p.playerId == "P1").companyProfits)
            {
                int raw = diversified.GetWindow(1, v.companyId, 30, 1)[0]; Check(v.returnBps == (raw < 0 ? raw * 80 / 100 : raw), "diversification reduces losses and leaves gains unchanged");
            }
        }
    }
}
