// 주문창 입력 초안(클라이언트 전용). 확정 전에는 서버에 보내지 않는다.
// 배열 순서 = 첫 기업/둘째 기업. 비중은 첫 기업 기준(5000/6000/7000), 둘째는 10000 - 첫 기업.
// 화면 정렬 때문에 순서가 바뀌지 않도록, 순서 변경은 SwapCompanies()로만 한다.
using System;
using System.Collections.Generic;
using System.Linq;
using BlackWhale.Shared;
using BlackWhale.UI.State;

namespace BlackWhale.UI.Presentation
{
    public sealed class InvestmentDraft
    {
        public string FirstCompanyId { get; private set; }
        public string SecondCompanyId { get; private set; }
        public int FirstWeightBps { get; private set; } = 5000;
        public int SecondWeightBps => UiRules.TotalWeightBps - FirstWeightBps;

        private List<Interval> _firstIntervals = new List<Interval>();
        private List<Interval> _secondIntervals = new List<Interval>();

        public IReadOnlyList<Interval> FirstIntervals => _firstIntervals;
        public IReadOnlyList<Interval> SecondIntervals => _secondIntervals;

        public InvestmentDraft(string firstCompanyId, string secondCompanyId)
        {
            FirstCompanyId = firstCompanyId;
            SecondCompanyId = secondCompanyId;
        }

        /// <summary>선택 순서대로 초안을 만든다. 선택이 2개가 아니면 null.</summary>
        public static InvestmentDraft FromSelections(IReadOnlyList<string> ownSelections)
        {
            if (ownSelections == null || ownSelections.Count != UiRules.CompaniesPerOrder) return null;
            return new InvestmentDraft(ownSelections[0], ownSelections[1]);
        }

        public bool SetFirstWeight(int weightBps)
        {
            if (!UiRules.FirstCompanyWeightOptionsBps.Contains(weightBps)) return false;
            FirstWeightBps = weightBps;
            return true;
        }

        public void SwapCompanies()
        {
            (FirstCompanyId, SecondCompanyId) = (SecondCompanyId, FirstCompanyId);
            (_firstIntervals, _secondIntervals) = (_secondIntervals, _firstIntervals);
        }

        public List<Interval> IntervalsOf(string companyId)
        {
            if (companyId == FirstCompanyId) return _firstIntervals;
            if (companyId == SecondCompanyId) return _secondIntervals;
            throw new ArgumentException("선택하지 않은 기업: " + companyId);
        }

        /// <summary>구간 추가. 사전검사에서 걸리면 추가하지 않고 오류 코드를 돌려준다.</summary>
        public string TryAddInterval(string companyId, int entryDay, int exitDay)
        {
            var list = IntervalsOf(companyId);
            var candidate = list.Select(DtoCopy.Copy).ToList();
            candidate.Add(new Interval { entryDay = entryDay, exitDay = exitDay });
            var error = OrderInputValidator.ValidateIntervals(candidate);
            if (error.Length == 0) list.Add(new Interval { entryDay = entryDay, exitDay = exitDay });
            return error;
        }

        public void RemoveInterval(string companyId, int index)
        {
            var list = IntervalsOf(companyId);
            if (index >= 0 && index < list.Count) list.RemoveAt(index);
        }

        public Investment[] ToInvestments()
        {
            return new[]
            {
                new Investment
                {
                    companyId = FirstCompanyId,
                    weightBps = FirstWeightBps,
                    intervals = _firstIntervals.Select(DtoCopy.Copy).ToArray()
                },
                new Investment
                {
                    companyId = SecondCompanyId,
                    weightBps = SecondWeightBps,
                    intervals = _secondIntervals.Select(DtoCopy.Copy).ToArray()
                }
            };
        }

        public string Validate()
            => OrderInputValidator.Validate(ToInvestments(), new[] { FirstCompanyId, SecondCompanyId });
    }
}
