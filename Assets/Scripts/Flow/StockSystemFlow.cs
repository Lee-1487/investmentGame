using System;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.Flow
{
    public sealed partial class GameRoom
    {
        private IStockMarketSystem Stock => market as IStockMarketSystem;
        private bool StockMode => Stock?.StockEnabled == true;
        private static bool IsStockCommand(string type) => type == StockRules.Trade || type == StockRules.Pass || type == StockRules.Choose || type == StockRules.Priority;
        private void SendAllViews() { foreach (var m in members) SendView(m.Id); }
        private void BeginStockDraft(double now) {
            Stock.BeginDraftPick(order[selectionIndex]);
            Enter(Protocol.Selection, now, order.Length * StockRules.DraftSeconds); SendAllViews();
        }
        private void RevealStockPortfolio(double now)
        {
            Stock.LockPortfolios(); ordersLocked = true;
            Enter(Protocol.Reveal, now, 10);
            Broadcast(new ServerMessage { type = "MARKET_REVEAL", revealedMarket = market.GetRevealedMarket(Context()) });
            SendAllViews();
        }
        private CommandResult ExecuteStockCommand(string player, Command command, double now)
        {
            if (command.type == StockRules.Choose)
            {
                if (phase != StockRules.AugmentPhase) return CommandResult.Fail(command, "WRONG_PHASE");
                if (command.payload == null) return CommandResult.Fail(command, "INVALID_REQUEST");
                var result = Stock.ChooseAugment(Context(player, command.requestId), command.payload.augmentId, command.payload.companyId);
                if (!result.Success) return CommandResult.Fail(command, result.ErrorCode);
                if (Stock.AllAugmentsChosen) { Enter(Protocol.Info, now, settings.InfoSeconds); SendAllViews(); } else SendView(player);
                return Success(command);
            }
            if (phase != Protocol.Selection) return CommandResult.Fail(command, "WRONG_PHASE");
            if (command.type == StockRules.Priority)
            {
                int next = Array.FindIndex(order, selectionIndex + 1, id => id == player);
                if (next <= selectionIndex + 1) return CommandResult.Fail(command, "NO_PRIORITY_SLOT");
                var result = Stock.ConsumePriority(player); if (!result.Success) return CommandResult.Fail(command, result.ErrorCode);
                string previous = order[next - 1]; order[next - 1] = player; order[next] = previous;
                PublishState(); SendView(player); return Success(command);
            }
            if (player != CurrentPlayer) return CommandResult.Fail(command, "NOT_YOUR_TURN");
            if (command.type == StockRules.Trade)
            {
                if (command.payload == null) return CommandResult.Fail(command, "INVALID_REQUEST");
                var result = Stock.TradeShares(Context(player, command.requestId), command.payload.shareOrders);
                if (!result.Success) { SendView(player); return CommandResult.Fail(command, result.ErrorCode); }
                PublishState(); SendAllViews();
                return Success(command);
            }
            Selected(now);
            return Success(command);
        }
    }
}
