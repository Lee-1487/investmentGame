using System;

namespace BlackWhale.Rules
{
    // rules-v2 (00_rules.json) values used by B. Field names match 00_rules.json.
    [Serializable]
    public sealed class InvestmentRules
    {
        public int daysPerTurn = 30;
        public int bundleSizeDays = 5;
        // 모든 플레이어가 게임을 시작할 때 보유하는 기본 자산입니다.
        // 게임의 자산 단위를 1억 원으로 맞추기 위해 시작값을 100,000,000으로 설정합니다.
        public long initialAssets = 100000000;
        public int maxPlayersPerCompany = 2;
        public int[] firstCompanyWeightOptionsBps = { 5000, 6000, 7000 };
        public int maxIntervalsPerCompany = 2; // v2 fixture default; 0 means unlimited in normal play.
        public bool entryAtOpen;
        public int minHoldingDays = 5;
        public int maxHoldingDays = 10;
        public int minEntryDay = 0;
        public int maxExitDay = 30;
        public int minimumCompanyReturnBps = -10000;
        public long minimumAssets = 0;
        public long comebackGrant = 1000;

        public const int FullWeightBps = 10000;
        public const int BundlesPerCompany = 6;
        public const int PublicBundlesPerCompany = 2;
        public static readonly string[] CompanyIds = { "AUTO", "FOOD", "AI", "BIO" };

        public InvestmentRules Copy()
        {
            var copy = (InvestmentRules)MemberwiseClone();
            copy.firstCompanyWeightOptionsBps = (int[])firstCompanyWeightOptionsBps.Clone();
            return copy;
        }
    }
}
