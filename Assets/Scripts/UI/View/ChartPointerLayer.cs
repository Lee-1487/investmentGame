// NEXT-D01/D02 그래프 위 포인터 입력(마우스 위치 툴팁·날짜 클릭).
using UnityEngine;
using UnityEngine.EventSystems;

namespace BlackWhale.UI.View
{
    /// <summary>그래프 위 포인터 → 날짜. Canvas 배율·카메라를 고려해 RectTransform 로컬 좌표로 바꾼 뒤 날짜를 계산한다.
    /// 봉이 아니라 날짜 칸 전체가 클릭을 받는다(값이 없는 날짜도 선택 가능).</summary>
    [RequireComponent(typeof(BarChartGraphic))]
    public sealed class ChartPointerLayer : MonoBehaviour, IPointerMoveHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public System.Action<int?> Hovered;
        /// <summary>day, 오른쪽 버튼 여부</summary>
        public System.Action<int, bool> Clicked;

        private BarChartGraphic _chart;

        private void Awake()
        {
            _chart = GetComponent<BarChartGraphic>();
            _chart.SetInteractive(true);
        }

        // 그래프 Awake 순서와 상관없이 항상 클릭을 받도록(미열람 칸 포함 날짜 칸 전체가 입력 영역).
        private void OnEnable()
        {
            if (_chart == null) _chart = GetComponent<BarChartGraphic>();
            if (_chart != null) _chart.SetInteractive(true);
        }

        private int? DayOf(PointerEventData e, Camera cam)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_chart.rectTransform, e.position, cam, out var local))
                return null;
            return _chart.DayAtLocalX(local.x);
        }

        public void OnPointerEnter(PointerEventData e) => Report(DayOf(e, e.enterEventCamera));
        public void OnPointerMove(PointerEventData e) => Report(DayOf(e, e.enterEventCamera));

        public void OnPointerExit(PointerEventData e)
        {
            _chart.SetHover(null);
            Hovered?.Invoke(null);
        }

        public void OnPointerClick(PointerEventData e)
        {
            var day = DayOf(e, e.pressEventCamera);
            if (!day.HasValue) return;
            if (e.button == PointerEventData.InputButton.Left) Clicked?.Invoke(day.Value, false);
            else if (e.button == PointerEventData.InputButton.Right) Clicked?.Invoke(day.Value, true);
        }

        private void Report(int? day)
        {
            _chart.SetHover(day);
            Hovered?.Invoke(day);
        }
    }
}
