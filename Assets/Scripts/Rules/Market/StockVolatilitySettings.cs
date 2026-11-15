using System;

namespace BlackWhale.Rules
{
    // Daily open-to-close maximum, in percentage points (100 bps = 1%). Never scales with turn.
    [Serializable]
    public sealed class StockVolatilitySettings
    {
        public int bankBps = 200, foodBps = 500, autoBps = 700, aiBps = 1500, bioBps = 2000, spaceBps = 2500;
        public int DailyLimitBps(string company)
        {
            switch (company)
            {
                case "BANK": return bankBps;
                case "FOOD": return foodBps;
                case "AUTO": return autoBps;
                case "AI": return aiBps;
                case "BIO": return bioBps;
                case "SPACE": return spaceBps;
                default: throw new ArgumentException("Unknown company.", nameof(company));
            }
        }
        public void SetDailyLimitBps(string company, int value)
        {
            if (value < 1 || value > 10000) throw new ArgumentOutOfRangeException(nameof(value), "하루 변동 한도는 0.01~100%입니다.");
            switch (company)
            {
                case "BANK": bankBps = value; break;
                case "FOOD": foodBps = value; break;
                case "AUTO": autoBps = value; break;
                case "AI": aiBps = value; break;
                case "BIO": bioBps = value; break;
                case "SPACE": spaceBps = value; break;
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
