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
                .Where(i => StockRules.CanShareRumor(company) && showRumors && i.companyId == company && i.targetPlayerId == player)
                .Select(i => new ChartPlanOverlay { playerId = i.senderId, entryDay = i.entryDay, exitDay = i.exitDay, rumor = true });
            return own.Concat(rumors).ToArray();
        }

        public static bool HiddenFinalSegment(VisibleCompany company, RevealedCompany revealed = null) =>
            (company?.companyId == "AUTO" || company?.companyId == "FOOD")
            && revealed == null && !InformationWindowRules.KnownDays(company)[26].HasValue;

        public static SegmentKnowledge[] RumorKnowledge(PlayerView view, PublicState state, bool showRumors)
        {
            var intents = showRumors ? view?.receivedTradeIntents ?? Array.Empty<TradeIntent>() : Array.Empty<TradeIntent>();
            return (state?.segmentKnowledge ?? Array.Empty<SegmentKnowledge>()).Concat(
                new[] { "AI", "BIO" }.SelectMany(company => Enumerable.Range(0, 6).Select(segment => new SegmentKnowledge {
                    companyId = company, startDay = segment * 5 + 1,
                    playerIds = intents.Where(i => i.companyId == company && i.targetPlayerId == view?.recipientPlayerId
                        && i.entryDay >= segment * 5 + 1 && i.entryDay <= segment * 5 + 5)
                        .Select(i => i.senderId).Distinct().ToArray()
                }))).ToArray();
        }

        public static string ProbabilityLabel(PublicState state, PortfolioState portfolio, string company)
        {
            if (!StockRules.CanShareRumor(company)) return "";
            var insight = portfolio?.insights?.FirstOrDefault(i => i.companyId == company);
            string up = insight?.upProbabilityBps >= 0 ? (insight.upProbabilityBps / 100m).ToString("0.##") + "%" : "—";
            string down = insight?.downProbabilityBps >= 0 ? (insight.downProbabilityBps / 100m).ToString("0.##") + "%" : "—";
            string segment = insight?.probabilitySegment >= 6 ? "최종 누적 · " : insight?.probabilitySegment >= 0 ? "다음 " + (insight.probabilitySegment + 1) + "구간 · " : "";
            return segment + "<b><color=#FF7078>" + (company == "BIO" ? "급등 " : "상승 ") + up + "</color>  <color=#69AFFF>" + (company == "BIO" ? "미발생 " : "하락 ") + down + "</color></b>";
        }
    }
}
