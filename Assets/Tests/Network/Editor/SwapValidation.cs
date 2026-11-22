using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Flow;
using BlackWhale.Rules;
using BlackWhale.Rules.Information;
using BlackWhale.Shared;
using BlackWhale.UI.State;

namespace BlackWhale.Tests
{
    /// <summary>[실험] 자산 교환(견제) 턴 검증.</summary>
    public static class SwapValidation
    {
        private static void Check(bool ok, string message) => RoomValidation.Check(ok, "SWAP: " + message);
        private static readonly string[] Players = { "P1", "P2", "P3", "P4" };

        public static string[] Run()
        {
            var names = new List<string>();
            Action<string, Action> run = (name, test) => { test(); names.Add("SWAP/" + name); };
            run("rotation-helpers", Helpers);
            Check(SwapRules.Turns.SequenceEqual(new[] { 5 }), "single swap turn at turn five");
            run("swap-turn-controls-next-account-and-pays-sabotage-bonus", SwapTurn);
            return names.ToArray();
        }

        private static void Helpers()
        {
            Check(SwapRules.AccountFor(Players, "P1") == "P2" && SwapRules.AccountFor(Players, "P4") == "P1", "A plays B, last wraps to first");
            Check(SwapRules.Bonus(-1000) == 1000 * SwapRules.BonusPercent / 100 && SwapRules.Bonus(500) == 0 && SwapRules.Bonus(0) == 0, "bonus only from losses");
            var state = new PublicState { swapTurn = true, swapControllerIds = Players, swapAccountIds = Players.Select(p => SwapRules.AccountFor(Players, p)).ToArray() };
            Check(SwapRules.ActingAccount(state, "P3") == "P4" && SwapRules.ControllerOf(state, "P1") == "P4", "client-side lookup");
            Check(SwapRules.ActingAccount(new PublicState(), "P3") == "P3", "normal turn plays own account");
            var copy = DtoCopy.Copy(state); copy.swapAccountIds[0] = "X";
            Check(state.swapAccountIds[0] == "P2" && DtoCopy.Copy(state).swapTurn, "public state copy keeps swap fields");
        }

        private static void SwapTurn()
        {
            int swapTurn = SwapRules.Turns.Length > 0 ? SwapRules.Turns[0] : 0;
            if (swapTurn == 0) return; // 교환 모드 꺼짐
            var clock = new TestClock();
            var market = MarketService.CreateStockSystem(7741);
            var info = new SharedInformationService(market);
            var room = new GameRoom("SWAP", market, info, clock, fixedOrderForFixture: true,
                settings: new GameSettings(5, 6, 2, enableProgressionFeatures: false));
            int seq = 0;
            Func<int, string, CommandPayload, CommandResult> send = (p, type, payload) => room.ProcessCommand("c" + p,
                new Command { type = type, turn = room.Snapshot().turn, requestId = "w" + (++seq), developerRevision = room.Snapshot().developerRevision, payload = payload });
            for (int i = 1; i <= 4; i++) room.Join("c" + i, "Player " + i);
            for (int i = 1; i <= 4; i++) send(i, "SET_READY", new CommandPayload { ready = true });
            send(1, "START_GAME", null);
            Action deadline = () => { var s = room.Snapshot(); clock.Seconds = s.phaseEndsAtServerSeconds; room.Tick(); };

            while (!(room.Snapshot().turn == swapTurn && room.Snapshot().phase == Protocol.Info))
            {
                Check(!room.Snapshot().swapTurn, "no swap before the swap turn");
                deadline();
            }
            var state = room.Snapshot();
            Check(state.swapTurn && state.swapTurns.Contains(swapTurn) && state.swapBonusPercent == SwapRules.BonusPercent, "swap turn is public from information phase");
            Check(state.swapControllerIds.SequenceEqual(Players) && state.swapAccountIds.SequenceEqual(new[] { "P2", "P3", "P4", "P1" }), "each seat controls the next seat");
            deadline();
            Check(room.Snapshot().phase == Protocol.Investment, "live trading in swap turn");
            room.DrainMessages();

            // 각 조종자는 이번 턴 30일 등락이 가장 나쁜 세 기업을 10주씩 산다.
            var worst = InvestmentRules.CompanyIds.Where(c => c != "BANK")
                .OrderBy(c => market.GetWindow(swapTurn, c, 30, 1)[0]).Take(3).ToArray();
            // 공급 20주 제한 때문에 두 명씩만 같은 기업을 꽉 채울 수 있으므로 P1·P2만 매매한다.
            foreach (var company in worst)
            {
                Check(send(1, StockRules.Position, new CommandPayload { shareOrders = new[] { new ShareOrder { companyId = company, targetShares = 10 } } }).success, "controller order accepted");
            }
            Check(market.GetPortfolio("P2").usedSlots == 30 && market.GetPortfolio("P1").usedSlots == 0, "P1's orders move P2's account, not P1's");
            var p1View = room.DrainMessages().Last(d => d.PlayerId == "P1" && d.Message.type == "PLAYER_VIEW").Message.playerView;
            Check(p1View.portfolio.usedSlots == 30 && p1View.recipientPlayerId == "P1", "P1's screen shows the controlled account's portfolio");
            Check(market.GetShareMarket().Single(c => c.companyId == worst[0]).owners.Single(o => o.playerId == "P2").shares == 10, "public market shows the account owner");

            while (room.Snapshot().phase != Protocol.Result) deadline();
            var board = market.GetScoreboard(swapTurn);
            var detail = market.GetSettlementDetail(swapTurn).ToDictionary(p => p.playerId);
            foreach (var row in board.rows)
            {
                string account = SwapRules.AccountFor(Players, row.playerId);
                Check(row.swapAccountId == account, "scoreboard names the controlled account");
                long accountOwn = detail[account].profit - board.rows.Single(r => r.playerId == account).swapBonus;
                Check(row.swapAccountProfit == accountOwn, "account profit excludes its owner's bonus");
                int worstRate = board.rows.Where(r => r.swapAccountProfit < 0).Select(r => r.swapAccountReturnBps).DefaultIfEmpty(0).Min();
                bool winner = row.swapAccountProfit < 0 && row.swapAccountReturnBps == worstRate;
                Check(row.swapBonus == (winner ? SwapRules.Bonus(accountOwn) : 0), "only the biggest sabotage earns the configured share of the loss");
                Check(row.swapAccountReturnBps == detail[account].turnReturnBps || board.rows.Single(r => r.playerId == account).swapBonus > 0, "account return is reported");
                Check(detail[row.playerId].endingAssets == row.totalAssets, "bonus is part of total assets");
            }
            var p1 = board.rows.Single(r => r.playerId == "P1");
            Check(p1.swapAccountProfit < 0 && p1.swapBonus > 0 && board.rows.Count(r => r.swapBonus > 0) == 1, "only P1, the biggest sabotage, gets the bonus");

            deadline(); // 다음 턴
            Check(room.Snapshot().turn == swapTurn + 1 && !room.Snapshot().swapTurn && room.Snapshot().swapAccountIds.Length == 0, "swap ends after its turn");
            while (room.Snapshot().phase != Protocol.Investment) deadline();
            Check(send(1, StockRules.Position, new CommandPayload { shareOrders = new[] { new ShareOrder { companyId = "AI", targetShares = 1 } } }).success
                && market.GetPortfolio("P1").usedSlots == 1 && market.GetPortfolio("P2").usedSlots == 0, "normal turn trades own account");
            var next = market.GetScoreboard(swapTurn);
            Check(next.rows.All(r => r.swapAccountId == board.rows.Single(b => b.playerId == r.playerId).swapAccountId), "settled swap board stays stable");
        }
    }
}
