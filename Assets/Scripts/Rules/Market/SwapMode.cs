using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.Rules
{
    /// <summary>
    /// [실험] 자산 교환(견제) 턴. 규칙과 조정값은 <see cref="SwapRules"/> 참고.
    /// 교환 턴에는 조종하는 사람(controller)의 실시간 매매가 바로 다음 자리 사람의 계좌(account)에 적용되고,
    /// 정산 때 그 계좌가 잃은 금액의 일부가 조종한 사람에게 견제 보너스로 들어간다.
    /// </summary>
    public sealed partial class MarketService : ISwapMarket
    {
        private readonly Dictionary<string, string> swapControl = new Dictionary<string, string>();
        private sealed class SwapOutcome { public string Account; public long AccountProfit, Bonus; }
        private readonly Dictionary<int, Dictionary<string, SwapOutcome>> swapOutcomes = new Dictionary<int, Dictionary<string, SwapOutcome>>();

        public bool SwapActive => swapControl.Count > 0;

        public void ConfigureSwap(string[] seatOrder)
        {
            swapControl.Clear();
            if (seatOrder == null || seatOrder.Length < 2 || !seatOrder.OrderBy(x => x).SequenceEqual(players.OrderBy(x => x))) return;
            foreach (var id in seatOrder) swapControl[id] = SwapRules.AccountFor(seatOrder, id);
        }

        public string SwapAccount(string controller)
            => controller != null && swapControl.TryGetValue(controller, out var account) ? account : controller;

        public string[] SwapControllers => swapControl.Keys.ToArray();

        private void ClearSwap() => swapControl.Clear();

        private void ApplySwapBonus(PlayerSettlement[] results)
        {
            swapOutcomes.Remove(turn);
            if (!SwapActive) return;
            var byId = results.ToDictionary(r => r.playerId);
            // 보너스를 더하기 전의 계좌 손익으로 모두 계산한 다음 적용한다.
            var outcomes = swapControl.Where(pair => byId.ContainsKey(pair.Key) && byId.ContainsKey(pair.Value))
                .ToDictionary(pair => pair.Key, pair => new SwapOutcome { Account = pair.Value, AccountProfit = byId[pair.Value].profit,
                    Bonus = SwapRules.Bonus(byId[pair.Value].profit) });
            foreach (var pair in outcomes)
            {
                var r = byId[pair.Key];
                r.endingAssets += pair.Value.Bonus;
                r.profit = r.endingAssets - r.startingAssets;
                r.turnReturnBps = Settlement.ReturnBps(r.startingAssets, r.endingAssets);
            }
            swapOutcomes[turn] = outcomes;
        }

        private SwapOutcome SwapOutcomeOf(string player)
            => swapOutcomes.TryGetValue(turn, out var map) && map.TryGetValue(player, out var o) ? o : null;
        private string SwapAccountOf(string player) => SwapOutcomeOf(player)?.Account;
        private long SwapProfitOf(string player) => SwapOutcomeOf(player)?.AccountProfit ?? 0;
        private long SwapBonusOf(string player) => SwapOutcomeOf(player)?.Bonus ?? 0;
    }
}
