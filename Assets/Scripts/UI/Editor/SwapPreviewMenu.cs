// 메뉴: BlackWhale > 교환 턴 바로 보기 (녹화용)
// 켜면(체크 표시) 개발용 태블릿 씬이 5턴(교환 턴)부터 시작한다. 1~4턴은 자동으로 넘어간다. 다시 누르면 꺼지고 1턴부터.
// 값은 PlayerPrefs에만 저장되고 코드·씬은 바뀌지 않는다.
using UnityEditor;
using UnityEngine;

namespace BlackWhale.UI.EditorTools
{
    public static class SwapPreviewMenu
    {
        private const string Path = "BlackWhale/교환 턴 바로 보기 (녹화용)";
        private const string Key = "BlackWhale.SwapPreview"; // SwapRules.PreviewPrefKey 와 같은 값

        [MenuItem(Path, false, 130)]
        public static void Toggle()
        {
            bool on = PlayerPrefs.GetInt(Key, 0) != 1;
            PlayerPrefs.SetInt(Key, on ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log(on ? "[BlackWhale] 교환 턴 바로 보기 켜짐: 다음 Play부터 5턴(교환 턴)으로 바로 시작합니다."
                         : "[BlackWhale] 교환 턴 바로 보기 꺼짐: 원래대로 1턴부터 시작합니다.");
            if (EditorApplication.isPlaying) Debug.Log("[BlackWhale] 지금 게임은 그대로입니다. Play를 멈췄다가 다시 누르세요.");
        }

        [MenuItem(Path, true)]
        public static bool ToggleValidate()
        {
            Menu.SetChecked(Path, PlayerPrefs.GetInt(Key, 0) == 1);
            return true;
        }
    }
}
