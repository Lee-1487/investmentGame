// [개발 전용] 대역 서비스가 쓸 fixture 읽기.
// 에디터에서만 프로젝트 루트의 '개발 정리/임시 데이터 및 검증용'을 읽는다(Assets 밖이라 빌드에 포함되지 않음).
// 방장 원본 시장·정답 파일을 클라이언트 Resources/StreamingAssets에 넣지 않는다.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.Presentation;
using UnityEngine;

namespace BlackWhale.UI.Dev
{
    public static class DevFixtures
    {
        /// <summary>fixture 기준 선택 순서(A_selection_replay). 봇이 가능하면 이 순서대로 고른다.</summary>
        public static readonly string[] ReplayPicks = { "AI", "SPACE", "BIO", "AI", "BIO", "AUTO", "BANK", "FOOD" };

        public static string FixtureDir
            => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "개발 정리", "임시 데이터 및 검증용");

        /// <summary>시장 원본과 기대 전광판을 읽는다. 실패하면 임의 시장(전광판 없음)으로 대체.</summary>
        public static void Load(out DevMarketFixture market, out Scoreboard scoreboard, out string source)
        {
#if UNITY_EDITOR
            try
            {
                market = JsonUtility.FromJson<DevMarketFixture>(File.ReadAllText(Path.Combine(FixtureDir, "B_market_server.json")));
                scoreboard = JsonUtility.FromJson<Scoreboard>(File.ReadAllText(Path.Combine(FixtureDir, "D_scoreboard_expected.json")));
                if (market?.companies != null && market.companies.Length == UiRules.CompanyIds.Length)
                {
                    source = "fixture(basic-turn-v2)";
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DevFixtures] fixture를 읽지 못해 임의 시장으로 시작합니다: " + e.Message);
            }
#endif
            market = Synthetic();
            scoreboard = null;
            source = "임의 시장(전광판 없음)";
        }

        private static DevMarketFixture Synthetic()
        {
            var companies = new List<DevMarketCompany>();
            int seed = 0;
            foreach (var id in UiRules.CompanyIds)
            {
                int cap = UiRules.AbsoluteChangeCapBps(id);
                var values = Enumerable.Range(0, UiRules.MaxDay + 1)
                    .Select(d => d == 0 ? 0 : (int)(Math.Sin(d * 0.35 + seed) * cap * 0.6)).ToArray();
                companies.Add(new DevMarketCompany { companyId = id, dailyChangesBps = values, publicBundleIndices = new[] { seed % 6, (seed + 3) % 6 } });
                seed++;
            }
            return new DevMarketFixture { companies = companies.ToArray() };
        }
    }
}
