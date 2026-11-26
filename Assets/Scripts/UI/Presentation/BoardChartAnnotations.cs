using System;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.UI.Presentation
{
    public sealed class ChartPlanOverlay
    {
        public string playerId;
        public int entryDay, exitDay;
        public bool rumor;
    }

    public static class BoardChartAnnotations
    {
        public static string DayNotes(ChartPlanOverlay[] notes, int day) => string.Join(" · ", (notes ?? Array.Empty<ChartPlanOverlay>())
            .Where(n => n.entryDay <= day && day <= n.exitDay)
            .Select(n => n.playerId + (n.rumor ? " 찌라시 " : " 예약 ") + n.entryDay + "~" + n.exitDay + "일"));

        public static ChartPlanOverlay[] Build(TradePlanBook plans, PlayerView view, string company, string player, bool showRumors)
        {
            if (view == null || plans.Turn != view.turn) return Array.Empty<ChartPlanOverlay>();
            var own = plans.Get(company).Intervals.Select(i => new ChartPlanOverlay { playerId = player,
                entryDay = i.entryDay, exitDay = i.exitDay });
            var rumors = (view.receivedTradeIntents ?? Array.Empty<TradeIntent>())
                .Where(i => showRumors && i.companyId == company && i.targetPlayerId == player)
                .Select(i => new ChartPlanOverlay { playerId = i.senderId, entryDay = i.entryDay, exitDay = i.exitDay, rumor = true });
            return own.Concat(rumors).ToArray();
        }

        public static string ProbabilityLabel(PublicState state, PortfolioState portfolio, string company)
        {
            if (company != "AI" && company != "BIO") return "";
            var insight = portfolio?.insights?.FirstOrDefault(i => i.companyId == company);
            if (insight == null) return "확률 수신 대기";
            if (state?.phase == Protocol.Reveal || state?.phase == Protocol.Result || state?.phase == Protocol.Ended)
                return "30일 종료 · 이번 라운드 확률 추첨 완료";
            if (state?.phase == Protocol.Investment && state.stockSegmentRevealing && state.stockSegmentIndex == 5)
                return "마지막 구간 공개 중 · 다음 구간 없음";
            int segment = state?.phase == Protocol.Investment ? state.stockSegmentIndex + (state.stockSegmentRevealing ? 2 : 1) : 1;
            string chance = (insight.probabilityBps / 100m).ToString("0.##") + "%";
            return segment + "구간 " + (company == "AI" ? "하락 " : "Lightening ") + chance + " · "
                + (segment == 1 ? "초기 확률" : "직전 구간 투자 반영")
                + (company == "AI" ? "\n이번 구간 투자말 4개 이상이면 하락 100%" : insight.probabilityBps == 0 ? "\n이미 발생 · 남은 구간 0%" : "\n기본 5% · 최대 40% · 30일당 1회");
        }
    }
}
