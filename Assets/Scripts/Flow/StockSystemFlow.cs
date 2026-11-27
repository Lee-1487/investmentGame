using BlackWhale.Shared;
using System;
using System.Collections.Generic;

namespace BlackWhale.Flow
{
    public sealed partial class GameRoom
    {
        private sealed class DeveloperPhase
        {
            public string Phase;
            public double Duration;
            public bool Settled, Locked;
            public Scoreboard Board;
            public Action RestoreMarket, RestoreInformation;
        }
        private readonly List<DeveloperPhase> developerCheckpoints = new List<DeveloperPhase>();
        private bool developerTimerRunning, restoringDeveloperPhase;
        private int developerRevision;
        private void CaptureDeveloperPhase(double duration)
        {
            if (mode != TimerMode.Developer || !StockMode || restoringDeveloperPhase) return;
            if (!(market is IDeveloperCheckpoint marketState) || !(information is IDeveloperCheckpoint informationState))
                throw new InvalidOperationException("Developer mode requires restorable stock/information services.");
            developerCheckpoints.Add(new DeveloperPhase { Phase = phase, Duration = duration, Settled = settled.Contains(turn),
                Locked = ordersLocked, Board = latestBoard, RestoreMarket = marketState.CaptureDeveloperCheckpoint(roomId),
                RestoreInformation = informationState.CaptureDeveloperCheckpoint(roomId) });
        }
        private CommandResult ExecuteDeveloperCommand(string player, Command command, double now)
        {
            if (mode != TimerMode.Developer || !StockMode) return CommandResult.Fail(command, "DEVELOPER_ONLY");
            if (player != "P1") return CommandResult.Fail(command, "HOST_ONLY");
            if (phase == Protocol.Lobby || phase == Protocol.Ended) return CommandResult.Fail(command, "WRONG_PHASE");
            if (command.type == "DEV_NEXT")
            {
                if (phase == Protocol.Investment)
                {
                    Stock.SimulateDay();
                    // A resumed timer starts its next full day at the newly confirmed close.
                    if (developerTimerRunning) phaseEnd = now + (30 - Stock.ProcessedDay) * lobbySettings.daySeconds;
                    if (Stock.ProcessedDay >= 30) NextPhase(now); else { PublishState(); SendAllViews(); }
                }
                else NextPhase(now);
                return Success(command);
            }
            if (command.type == "DEV_TIMER")
            {
                if (phase != Protocol.Investment) return CommandResult.Fail(command, "WRONG_PHASE");
                developerTimerRunning = !developerTimerRunning;
                phaseEnd = now + (30 - Stock.ProcessedDay) * lobbySettings.daySeconds;
                PublishState(); SendAllViews(); return Success(command);
            }
            if (command.type != "DEV_PREVIOUS" && command.type != "DEV_RESET") return CommandResult.Fail(command, "INVALID_REQUEST");
            int index = developerCheckpoints.Count - (command.type == "DEV_PREVIOUS" ? 2 : 1);
            if (index < 0) return CommandResult.Fail(command, "NO_PREVIOUS_PHASE");
            var checkpoint = developerCheckpoints[index];
            checkpoint.RestoreMarket(); checkpoint.RestoreInformation();
            if (checkpoint.Settled) settled.Add(turn); else settled.Remove(turn);
            ordersLocked = checkpoint.Locked; latestBoard = checkpoint.Board;
            developerCheckpoints.RemoveRange(index + 1, developerCheckpoints.Count - index - 1);
            developerRevision++; cache.Clear(); counts.Clear();
            restoringDeveloperPhase = true;
            try { Enter(checkpoint.Phase, now, checkpoint.Duration); }
            finally { restoringDeveloperPhase = false; }
            SendAllViews();
            if (phase == Protocol.Reveal) Broadcast(new ServerMessage { type = "MARKET_REVEAL", revealedMarket = market.GetRevealedMarket(Context()) });
            if (latestBoard != null) Broadcast(new ServerMessage { type = "SCOREBOARD", scoreboard = latestBoard });
            return Success(command);
        }
        private IStockMarketSystem Stock => market as IStockMarketSystem;
        private bool StockMode => Stock?.StockEnabled == true;
        private static bool IsStockCommand(string type) => type == StockRules.Position || type == StockRules.Trade || type == StockRules.Pass || type == StockRules.Choose || type == StockRules.Priority;
        // 증강 선택은 각자 자기 것을 고른다. 교환은 정보 탐색부터 적용.
        private bool SwapActive => StockMode && phase != StockRules.AugmentPhase && market is ISwapMarket swap && swap.SwapActive;
        private string Account(string player) => SwapActive ? ((ISwapMarket)market).SwapAccount(player) : player;
        /// <summary>[실험] 녹화용: 앞 턴들을 자동으로 진행해 target 턴의 첫 단계에서 멈춘다.
        /// 증강은 첫 후보가 자동 선택되고, 아무도 매매하지 않으므로 앞 턴 자금은 모두 BANK에 있다.</summary>
        private void SkipToTurn(int target)
        {
            target = Math.Min(target, settings.Turns);
            for (int guard = 0; turn < target && phase != Protocol.Ended && guard < 100; guard++) NextPhase(Now);
            PublishState(); SendAllViews();
        }
        private void SendAllViews() { foreach (var m in members) SendView(m.Id); }
        private void RevealStockPortfolio(double now)
        {
            Stock.LockPortfolios(); ordersLocked = true;
            Enter(Protocol.Reveal, now, 10);
            Broadcast(new ServerMessage { type = "MARKET_REVEAL", revealedMarket = market.GetRevealedMarket(Context()) });
            SendAllViews();
        }
        private CommandResult ExecuteStockCommand(string player, Command command, double now)
        {
            if (command.type == StockRules.Position)
            {
                if (phase != Protocol.Investment) return CommandResult.Fail(command, "WRONG_PHASE");
                var orders = command.payload?.shareOrders;
                if (orders?.Length != 1) return CommandResult.Fail(command, "INVALID_SHARE_ORDER");
                // 교환 턴: 내 주문은 내가 조종 중인 계좌에 들어간다.
                var adjusted = Stock.SetPosition(Context(Account(player), command.requestId), orders[0]);
                if (!adjusted.Success) return CommandResult.Fail(command, adjusted.ErrorCode);
                PublishState(); SendAllViews(); return Success(command);
            }
            if (command.type == StockRules.Choose)
            {
                if (phase != StockRules.AugmentPhase) return CommandResult.Fail(command, "WRONG_PHASE");
                if (command.payload == null) return CommandResult.Fail(command, "INVALID_REQUEST");
                var result = Stock.ChooseAugment(Context(player, command.requestId), command.payload.augmentId, command.payload.companyId);
                if (!result.Success) return CommandResult.Fail(command, result.ErrorCode);
                WindowInfo.GrantAnalystCoins(Context(), player); PublishState();
                if (Stock.AllAugmentsChosen) { Enter(Protocol.Info, now, settings.InfoSeconds); SendAllViews(); } else SendView(player);
                return Success(command);
            }
            return CommandResult.Fail(command, "WRONG_PHASE");
        }
    }
}
