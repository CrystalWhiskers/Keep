using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Strategy
{
    /// <summary>Builds uGUI elements in code: generated rounded-rect sprites, OS fonts, and placement helpers.</summary>
    public static class UIKit
    {
        static Font font, iconFont;
        static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        public static Font Font
        {
            get
            {
                if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI", "Helvetica Neue", "Arial" }, 32);
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static Font IconFont
        {
            get
            {
                if (iconFont == null) iconFont = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI Symbol", "Apple Symbols", "Segoe UI", "Arial" }, 32);
                if (iconFont == null) iconFont = Font;
                return iconFont;
            }
        }

        // ------------------------------------------------------------ sprites

        /// <summary>Nine-sliced rounded rectangle. stroke = 0 for a fill, otherwise a ring of that width.</summary>
        public static Sprite Round(int radius, float stroke = 0f)
        {
            string key = $"r{radius}_{stroke}";
            if (sprites.TryGetValue(key, out var s)) return s;
            int size = radius * 2 + 4;
            s = Make(size, size, (x, y) => RoundedAlpha(x, y, size, size, radius, stroke));
            s = Sprite.Create(s.texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
            sprites[key] = s;
            return s;
        }

        /// <summary>Circle (or ring) drawn at its real pixel size so strokes stay crisp.</summary>
        public static Sprite Circle(int diameter, float stroke = 0f)
        {
            string key = $"c{diameter}_{stroke}";
            if (sprites.TryGetValue(key, out var s)) return s;
            int size = diameter + 2;
            s = Make(size, size, (x, y) => RoundedAlpha(x, y, size, size, diameter / 2f, stroke));
            sprites[key] = s;
            return s;
        }

        static float RoundedAlpha(float px, float py, int w, int h, float r, float stroke)
        {
            var c = new Vector2(w / 2f, h / 2f);
            var he = new Vector2(w / 2f - 1f, h / 2f - 1f);
            var p = new Vector2(Mathf.Abs(px - c.x), Mathf.Abs(py - c.y)) - (he - new Vector2(r, r));
            float d = new Vector2(Mathf.Max(p.x, 0), Mathf.Max(p.y, 0)).magnitude + Mathf.Min(Mathf.Max(p.x, p.y), 0) - r;
            float a = Mathf.Clamp01(0.5f - d);
            if (stroke > 0) a *= Mathf.Clamp01(0.5f + d + stroke);
            return a;
        }

        static Sprite Make(int w, int h, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(alpha(x + 0.5f, y + 0.5f) * 255));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------ elements

        /// <summary>Rect anchored to its parent's top-left corner, positioned like the mockup (y grows downward).</summary>
        public static RectTransform TL(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>Stretches to fill the parent.</summary>
        public static RectTransform Fill(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Img(RectTransform rt, Sprite sprite, Color color, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>Filled rounded rect with a 1.5px border on top, like every panel in the mockup.</summary>
        public static Panel Panel(Transform parent, string name, float x, float y, float w, float h,
            Color fill, Color stroke, int radius = 10, bool raycast = true)
        {
            var rt = TL(parent, name, x, y, w, h);
            var p = new Panel { Rect = rt, Fill = Img(rt, Round(radius), fill, raycast) };
            p.Stroke = Img(Fill(rt, "Stroke"), Round(radius, 1.5f), stroke);
            return p;
        }

        public static Text Label(Transform parent, string text, float x, float y, float w, float h, int size, Color color,
            FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft, bool icon = false)
        {
            var rt = TL(parent, "Text", x, y, w, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = icon ? IconFont : Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Text Label(RectTransform fill, string text, int size, Color color, FontStyle style, TextAnchor align, bool icon = false)
        {
            var t = fill.gameObject.AddComponent<Text>();
            t.font = icon ? IconFont : Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static UIButton Button(Panel p)
        {
            p.Fill.raycastTarget = true;
            return p.Rect.gameObject.AddComponent<UIButton>();
        }

        public static Color A(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }

    public class Panel
    {
        public RectTransform Rect;
        public Image Fill, Stroke;

        public void Colors(Color fill, Color stroke)
        {
            Fill.color = fill;
            Stroke.color = stroke;
        }

        public void Show(bool v)
        {
            if (Rect.gameObject.activeSelf != v) Rect.gameObject.SetActive(v);
        }
    }
}
