// 내가 연 정보 구간(직접 산 구간 + 공용 공개 때 내가 고른 구간)을 차트에 계속 표시하기 위한 계산(화면 객체 의존 없음).
// - 개인뷰의 visibleWindows 중 내 기록(PURCHASED·ABILITY 등, PUBLIC·AUGMENT 제외)과
//   공개 상태의 publicInformation 중 playerId가 나인 것만 쓴다. 다른 사람이 산 구간은 넣지 않는다.
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using UnityEngine;

namespace BlackWhale.UI.Presentation
{
    public static class MyInformationWindows
    {
        public static List<Vector2Int> For(PublicState state, PlayerView view, string me, string companyId)
        {
            var result = new List<Vector2Int>();
            if (string.IsNullOrEmpty(companyId)) return result;
            var company = view?.companies?.FirstOrDefault(c => c.companyId == companyId);
            foreach (var w in company?.visibleWindows ?? new VisibleInformationWindow[0])
            {
                if (w == null || w.source == "PUBLIC" || w.source == "AUGMENT") continue;
                int len = w.dailyChangesBps != null && w.dailyChangesBps.Length > 0 ? w.dailyChangesBps.Length : InformationWindowRules.Length;
                result.Add(new Vector2Int(w.startDay, Mathf.Min(30, w.startDay + len - 1)));
            }
            if (!string.IsNullOrEmpty(me))
                foreach (var c in state?.publicInformation ?? new PublicInformationChoice[0])
                    if (c != null && c.playerId == me && c.companyId == companyId)
                        result.Add(new Vector2Int(c.startDay, Mathf.Min(30, c.startDay + InformationWindowRules.Length - 1)));
            return result.Where(r => r.x >= 0 && r.y >= r.x).Distinct().OrderBy(r => r.x).ToList();
        }
    }
}
