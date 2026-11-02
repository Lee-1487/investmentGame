// 숫자 표시 형식. bps는 100으로 나눠 %로, 금액은 천 단위 구분.
// 예: 4280 bps -> "+42.8%", -1100 bps -> "-11%", 14280 -> "14,280"
using System.Globalization;

namespace BlackWhale.UI.Presentation
{
    public static class DisplayFormat
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static double BpsToPercent(int bps) => bps / 100.0;

        public static string Percent(int bps, bool withSign = true)
        {
            string number = BpsToPercent(bps).ToString("0.##", Inv);
            if (withSign && bps > 0) number = "+" + number;
            return number + "%";
        }

        public static string Money(long amount) => amount.ToString("N0", Inv);

        public static string SignedMoney(long amount)
            => (amount > 0 ? "+" : "") + Money(amount);

        public static string Weight(int weightBps) => (weightBps / 100).ToString(Inv) + "%";
    }
}
