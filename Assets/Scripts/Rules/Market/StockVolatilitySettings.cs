using System;

namespace BlackWhale.Rules
{
    // Ordinary daily movement in percentage points (100 bps = 1%). Rule-based surges use a separate multiplier.
    [Serializable]
    public sealed class StockVolatilitySettings
    {
        public int foodBps = 500, autoBps = 700, aiBps = 1500, bioBps = 2000;
        public int DailyLimitBps(string company)
        {
            switch (company)
            {
                case "FOOD": return foodBps;
                case "AUTO": return autoBps;
                case "AI": return aiBps;
                case "BIO": return bioBps;
                default: throw new ArgumentException("Unknown company.", nameof(company));
            }
        }
        public void SetDailyLimitBps(string company, int value)
        {
            if (value < 1 || value > 10000) throw new ArgumentOutOfRangeException(nameof(value), "하루 변동 한도는 0.01~100%입니다.");
            switch (company)
            {
                case "FOOD": foodBps = value; break;
                case "AUTO": autoBps = value; break;
                case "AI": aiBps = value; break;
                case "BIO": bioBps = value; break;
                default: throw new ArgumentException("Unknown company.", nameof(company));
            }
        }
        public StockVolatilitySettings Copy()
        {
            var copy = new StockVolatilitySettings();
            foreach (string company in InvestmentRules.CompanyIds) copy.SetDailyLimitBps(company, DailyLimitBps(company));
            return copy;
        }
    }
}
