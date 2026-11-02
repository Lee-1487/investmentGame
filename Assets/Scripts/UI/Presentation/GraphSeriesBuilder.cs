// 일별 그래프 데이터 생성.
// - 0일차는 항상 0% (전원 공개 기준값)
// - 묶음 k의 날짜 = startDay + i (5개 배열의 인덱스 0은 0일차가 아니다)
// - 보이지 않는 날짜는 '값 없음'(null). 0%로 채우지 않는다.
// - 연속으로 값이 있는 날짜끼리만 선으로 잇는다. 숨은 구간을 건너뛰어 잇지 않는다.
// - 축 범위는 비공개 원본이 아니라 규칙 설정값(기준 대비 상한) 또는 현재 열람값으로 정한다.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;

namespace BlackWhale.UI.Presentation
{
    public struct GraphPoint
    {
        public int Day;
        public int Bps;
        public GraphPoint(int day, int bps) { Day = day; Bps = bps; }
        public float Percent => Bps / 100f;
        public override string ToString() => Day + ":" + Bps;
    }

    public struct LockedRange
    {
        public int BundleIndex;
        public int StartDay;
        public int EndDay;
    }

    public static class GraphSeriesBuilder
    {
        public const int DayCount = UiRules.MaxDay + 1; // 0~30

        public static int BundleStartDay(int bundleIndex) => bundleIndex * UiRules.BundleSizeDays + 1;
        public static int BundleEndDay(int bundleIndex) => BundleStartDay(bundleIndex) + UiRules.BundleSizeDays - 1;

        /// <summary>날짜(0~30)별 알려진 값. 모르면 null.</summary>
        public static int?[] KnownValues(VisibleCompany company)
        {
            var values = new int?[DayCount];
            values[0] = 0;
            if (company?.visibleBundles == null) return values;

            foreach (var b in company.visibleBundles)
            {
                if (b?.dailyChangesBps == null) continue;
                for (int i = 0; i < b.dailyChangesBps.Length; i++)
                {
                    int day = b.startDay + i;
                    if (day >= 0 && day < DayCount) values[day] = b.dailyChangesBps[i];
                }
            }
            return values;
        }

        /// <summary>선으로 이을 연속 구간 목록. 점이 1개뿐인 구간은 점으로만 그린다.</summary>
        public static List<List<GraphPoint>> Segments(VisibleCompany company)
        {
            var values = KnownValues(company);
            var segments = new List<List<GraphPoint>>();
            List<GraphPoint> current = null;

            for (int day = 0; day < DayCount; day++)
            {
                if (values[day].HasValue)
                {
                    if (current == null)
                    {
                        current = new List<GraphPoint>();
                        segments.Add(current);
                    }
                    current.Add(new GraphPoint(day, values[day].Value));
                }
                else
                {
                    current = null;
                }
            }
            return segments;
        }

        /// <summary>시장 공개(MARKET_REVEAL) 후 전체 31일 값을 하나의 구간으로. 공개 전에는 호출하지 않는다.</summary>
        public static List<GraphPoint> RevealedSeries(RevealedCompany revealed)
        {
            var list = new List<GraphPoint>();
            if (revealed?.dailyChangesBps == null) return list;
            for (int day = 0; day < revealed.dailyChangesBps.Length && day < DayCount; day++)
                list.Add(new GraphPoint(day, revealed.dailyChangesBps[day]));
            return list;
        }

        /// <summary>잠금 표시할 묶음(아직 열람하지 않은 5일 구간).</summary>
        public static List<LockedRange> LockedRanges(VisibleCompany company)
        {
            var visible = new HashSet<int>(company?.visibleBundles?.Select(b => b.bundleIndex) ?? Enumerable.Empty<int>());
            var result = new List<LockedRange>();
            for (int k = 0; k < UiRules.BundleCount; k++)
            {
                if (visible.Contains(k)) continue;
                result.Add(new LockedRange { BundleIndex = k, StartDay = BundleStartDay(k), EndDay = BundleEndDay(k) });
            }
            return result;
        }

        /// <summary>규칙 설정값 기준 Y축 범위(±상한 bps). 비공개 원본을 읽지 않는다.</summary>
        public static int AxisLimitBps(string companyId) => UiRules.AbsoluteChangeCapBps(companyId);

        public static bool TryGetSource(VisibleCompany company, int bundleIndex, out string source)
        {
            var b = company?.visibleBundles?.FirstOrDefault(x => x.bundleIndex == bundleIndex);
            source = b?.source;
            return b != null;
        }
    }
}
