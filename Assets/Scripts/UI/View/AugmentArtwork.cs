using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlackWhale.UI.View
{
    /// <summary>
    /// 증강 ID에 대응하는 PNG를 Resources에서 읽어 UI에 표시하는 공통 도우미입니다.
    /// 이미지가 없더라도 기존 텍스트 UI가 계속 동작하도록 null을 허용합니다.
    /// </summary>
    internal static class AugmentArtwork
    {
        private const string ResourcePrefix = "Augments/";
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite SpriteOf(string augmentId)
        {
            if (string.IsNullOrEmpty(augmentId)) return null;
            if (Cache.TryGetValue(augmentId, out var cached)) return cached;

            var texture = Resources.Load<Texture2D>(ResourcePrefix + augmentId);
            if (texture == null)
            {
                Cache[augmentId] = null;
                return null;
            }

            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "Augment_" + augmentId;
            Cache[augmentId] = sprite;
            return sprite;
        }

        public static RectTransform Create(Transform parent, string augmentId, float size)
        {
            var frame = UiKit.Panel(parent, "AugmentArtwork_" + augmentId, Palette.CardBorder, rounded: true);
            frame.rectTransform.sizeDelta = new Vector2(size, size);

            var image = UiKit.Panel(frame.transform, "Image", Color.white);
            image.sprite = SpriteOf(augmentId);
            image.preserveAspect = true;
            UiKit.Stretch(image.rectTransform, 2, 2, 2, 2);
            return frame.rectTransform;
        }
    }
}
