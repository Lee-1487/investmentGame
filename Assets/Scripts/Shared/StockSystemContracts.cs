using System;
using System.Linq;

namespace BlackWhale.Shared
{
    public static class StockRules
    {
        public const int Turns = 6, Supply = 20, Slots = 3, PersonalLimit = 3;
        public const long InitialAssets = 100000000;
        public const int DecisionSeconds = 10, SegmentRevealSeconds = 2;
        public const string ShareInformation = "SHARE_INFORMATION", SendPlan = "SEND_TRADE_INTENT";
        public const string RespondExchange = "RESPOND_INFORMATION_EXCHANGE";
        public const int DraftSeconds = 7, DraftRounds = 2, SharesPerPick = 15, DaySeconds = 2;
        public const int InvestmentSeconds = 6 * (DecisionSeconds + SegmentRevealSeconds);
        // ProcessedDay is the number of completed closes; highlight the day currently in progress.
        public static int? CurrentDay(PublicState state, PortfolioState portfolio) => state?.stockSystem == true
            && state.phase == Protocol.Investment && state.stockSegmentRevealing && portfolio != null ? Math.Min(30, Math.Max(1, portfolio.processedDay + 1)) : (int?)null;
        public const string AugmentPhase = "AUGMENT_SELECTION";
        public const string Position = "SET_STOCK_POSITION";
        public const string Trade = "DRAFT_SHARES", Pass = "PASS_DRAFT", Choose = "CHOOSE_AUGMENT", Priority = "USE_DRAFT_PRIORITY";
        public static readonly string[] Augments = { "ANALYST", "BIO_RESEARCH", "DIVERSIFY", "LONG_TERM", "HIDDEN_LOSS_CONTROL", "CONCENTRATION" };
        public static int ShareLimit(string[] augments, string company)
        {

            if (company == "BIO" && augments?.Contains("BIO_RESEARCH") == true) return 15;
            return augments?.Contains("CONCENTRATION") == true ? 12 : PersonalLimit;
        }
        public static string Name(string id)
        {
            string[] names = { "내부 분석가", "바이오 연구원", "위험 분산", "장기 투자의 힘", "HIDDEN 손실 제어", "집중 투자자" };
            int i = Array.IndexOf(Augments, id); return i < 0 ? id ?? "" : names[i];
        }
        public static string Description(string id)
        {
            switch (id)
            {
                case "PRIORITY": return "게임당 1회, 아직 시작하지 않은 내 차례를 한 자리 앞당깁니다.";
                case "FLEXIBLE": return "여러 기업 동시 주문은 기본 규칙으로 통합되었습니다. 이 증강은 새 후보로 등장하지 않습니다.";
                case "OPPORTUNITY": return "유효 매수가 잔여 물량 부족으로 실패하면 턴당 1회, 다음 정보 구매에 쓸 2일 할인권을 받습니다.";
                case "ANALYST": return "매 턴 정보 코인을 2개 더 받습니다. 선택한 턴부터 적용됩니다.";
                case "TREND": return "직전 턴 마지막 3일 연속 상승 기업의 이번 턴 1~3일 정보를 추가 확인합니다.";
                case "BIO_RESEARCH": return "BIO 주식을 최대 15주까지 보유할 수 있습니다.";
                case "DIVERSIFY": return "기업별 3주 이상으로 3개 이상 보유하면 각 기업 손실의 20%를 완화합니다.";
                case "LONG_TERM": return "한 턴 안에서 같은 기업을 누적 10일 이상 보유하면 해당 기업의 양수 수익을 총 3배로 받습니다.";
                case "HIDDEN_LOSS_CONTROL": return "기업의 hidden 구간(26~30일)에서 보유한 날의 손실은 20%, 수익은 50%만 적용합니다. 22~25일 전조에는 적용하지 않습니다.";
                case "CONCENTRATION": return "모든 기업의 개인 보유 한도를 12주로 높입니다. BIO 15주 증강이 있으면 더 높은 한도를 적용합니다.";
                default: return "";
            }
        }
    }
    [Serializable] public sealed class ShareOrder { public string companyId; public int targetShares; }
    [Serializable] public struct StockFill { public string playerId; public int day, quantity; }
    [Serializable] public sealed class ShareOwner { public string playerId; public int shares, reserved; }
    [Serializable] public sealed class CompanyShareState
    {
        public string companyId;
        public int totalShares = StockRules.Supply;
        public ShareOwner[] owners;
        public StockFill[] fills;
    }
    [Serializable] public sealed class PortfolioHolding { public string companyId; public int shares, activeShares, consecutiveTurns; }
    [Serializable] public sealed class StockInsight { public string companyId, message; public int probabilityBps; }
    [Serializable] public sealed class TradeIntent
    {
        public string senderId, targetPlayerId, companyId;
        public int entryDay, exitDay;
    }
    [Serializable] public sealed class InformationExchange
    {
        public string id, senderId, targetPlayerId, offeredCompanyId, requestedCompanyId, status;
        public int offeredStartDay, requestedStartDay;
        public InformationExchange Copy() => (InformationExchange)MemberwiseClone();
    }
    [Serializable] public sealed class StockSegmentRecord
    {
        public int segmentIndex;
        public CompanyShareState[] companies;
    }
    [Serializable] public sealed class PortfolioState
    {
        public int processedDay;
        public int capacity = StockRules.Slots, usedSlots, informationDiscount;
        public int draftSharesRemaining = StockRules.SharesPerPick;
        public PortfolioHolding[] holdings;
        public string[] augments, candidates;
        public bool augmentChosen, priorityUsed;
        public string concentrationCompany;
        public StockInsight[] insights;
        public int CashSlots => capacity - usedSlots;
        public int AvailableToBuy(CompanyShareState market)
        {
            if (market == null) return 0;
            int mine = holdings?.FirstOrDefault(h => h.companyId == market.companyId)?.activeShares ?? 0;
            int supply = market.totalShares - (market.owners?.Sum(o => o.shares) ?? 0);
            return Math.Max(0, Math.Min(CashSlots, Math.Min(LimitFor(market.companyId) - mine, supply)));
        }
        public int LimitFor(string company) => StockRules.ShareLimit(augments, company);
    }
    public interface IStockMarketSystem
    {
        bool StockEnabled { get; }
        CompanyShareState[] GetShareMarket();
        PortfolioState GetPortfolio(string playerId);
        void BeginDraftPick(string playerId);
        RuleResult TradeShares(RequestContext context, ShareOrder[] orders);
        void BeginAugmentSelection();
        RuleResult ChooseAugment(RequestContext context, string id, string company);
        bool AllAugmentsChosen { get; }
        void CompleteAugmentSelection();
        RuleResult ConsumePriority(string playerId);
        int ProcessedDay { get; }
        void BeginLiveTrading();
        bool DecisionOpen { get; }
        void BeginDecision();
        void RevealSegment();
        StockSegmentRecord[] SegmentHistory();
        RuleResult SetPosition(RequestContext context, ShareOrder order);
        void SimulateDay();
        void LockPortfolios();
        int InformationDiscount(string playerId);
        void ConsumeInformationDiscount(string playerId, int days);
        VisibleInformationWindow[] AnalysisWindows(string playerId);
    }
}
