using BlackWhale.Rules;
using UnityEngine;

namespace BlackWhale.UI.Dev
{
    public static class StockPatternLibraryResource
    {
        public const string ResourceName = "StockPatternLibrary";
        public static StockPatternLibrary Load()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            if (asset == null) return new StockPatternLibrary();
            try
            {
                var library = JsonUtility.FromJson<StockPatternLibrary>(asset.text);
                library.UpgradeLegacy();
                return library.Copy();
            }
            catch (System.Exception exception)
            {
                Debug.LogError("주가 패턴 라이브러리를 읽을 수 없습니다: " + exception.Message);
                return new StockPatternLibrary();
            }
        }
    }
}
