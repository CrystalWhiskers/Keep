using UnityEngine;
using UnityEngine.UI;

namespace Strategy
{
    /// <summary>
    /// Circular minimap. The map is drawn into a small texture, then rotated 45° and squashed to match the
    /// isometric camera, so it reads as the same diamond as the board. White box = camera view.
    /// </summary>
    public class Minimap
    {
        const int S = 8;
        readonly GameManager gm;
        readonly Texture2D tex;
        readonly Color32[] px;
        readonly RectTransform imgRt;
        readonly int w, h;

        static readonly Color32 Unexplored = Palette.Hex("#1a2428");
        static readonly Color32 Explored = Palette.Hex("#26393b");
        static readonly Color32 VisibleC = Palette.Hex("#3d5759");
        static readonly Color32 RockC = Palette.Hex("#5c7074");
        static readonly Color32 Grid = Palette.Hex("#10171a");
        static readonly Color32 WaterC = Palette.Water;
        static readonly Color32 ForestC = Palette.Hex("#2c5544");
        static readonly Color32 RoadC = Palette.Hex("#5d6158");

        public Minimap(RectTransform area, GameManager gm)
        {
            this.gm = gm;
            var m = gm.Model;
            w = m.W * S;
            h = m.H * S;

            UIKit.Img(area, UIKit.Circle(250), Palette.Hex("#0b1013"), true);
            area.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            var squash = UIKit.Fill(area, "Squash");
            squash.localScale = new Vector3(1f, Mathf.Sin(CameraRig.Pitch * Mathf.Deg2Rad), 1f);

            imgRt = UIKit.Fill(squash, "Map");
            imgRt.anchorMin = imgRt.anchorMax = new Vector2(0.5f, 0.5f);
            imgRt.sizeDelta = new Vector2(150, 150);
            imgRt.localRotation = Quaternion.Euler(0, 0, CameraRig.Yaw);

            tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            px = new Color32[w * h];
            var raw = imgRt.gameObject.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;

            var button = area.gameObject.AddComponent<UIButton>();
            button.OnClickAt = e =>
            {
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(imgRt, e.position, null, out var local)) return;
                var uv = local / imgRt.rect.size + new Vector2(0.5f, 0.5f);
                gm.Rig.JumpTo(new Vector3(uv.x * m.W - 0.5f, 0, uv.y * m.H - 0.5f));
            };
        }

        public void Draw()
        {
            var m = gm.Model;
            for (int x = 0; x < m.W; x++)
                for (int y = 0; y < m.H; y++)
                {
                    var t = m.Tiles[x, y];
                    Color32 c = !t.Explored ? Unexplored : t.Rock ? RockC : t.Water ? WaterC : t.Forest ? ForestC : t.Road ? RoadC
                        : t.Visible ? VisibleC : Explored;
                    if (t.Explored && !t.Visible && !t.Rock) c = Color32.Lerp(c, Explored, 0.4f);
                    if (t.Explored && !t.Rock && !t.Water && t.Height > 0) c = Color32.Lerp(c, Color.white, 0.1f * t.Height);
                    for (int i = 1; i < S; i++)
                        for (int j = 1; j < S; j++)
                            px[(y * S + j) * w + x * S + i] = c;
                    for (int i = 0; i < S; i++)
                    {
                        px[(y * S) * w + x * S + i] = Grid;
                        px[(y * S + i) * w + x * S] = Grid;
                    }
                    if (t.Explored && t.Gold > 0) Dot(t.Pos, 1.6f, Palette.Gold);
                    if (t.Explored && t.Poi != PoiType.None)
                        Diamond(t.Pos, 3, t.Owner.HasValue ? Palette.Of(t.Owner.Value) : t.Poi == PoiType.Spring ? Palette.Cyan : Palette.Neutral);
                }

            foreach (var b in m.Buildings)
            {
                if (!m.T(b.Pos).Explored) continue;
                Square(b.Pos, 3, Palette.Of(b.Faction));
            }
            foreach (var u in m.Units)
            {
                if (u.Faction == Faction.Enemy && !m.T(u.Pos).Visible) continue;
                var c = u.Faction == Faction.Enemy ? Palette.Red
                    : m.Current == Faction.Player && !m.CanAct(u) ? Palette.Grey : Palette.Cyan;
                Dot(u.Pos, 2.6f, c);
            }

            var corners = gm.Rig.GroundCorners();
            for (int i = 0; i < 4; i++) Line(corners[i], corners[(i + 1) % 4], Color.white);

            tex.SetPixels32(px);
            tex.Apply(false);
        }

        void Dot(Vector2Int p, float r, Color c)
        {
            float cx = p.x * S + S / 2f, cy = p.y * S + S / 2f;
            Color32 c32 = c;
            for (int x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
                for (int y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r * r) px[y * w + x] = c32;
                }
        }

        void Diamond(Vector2Int p, int r, Color c)
        {
            int cx = p.x * S + S / 2, cy = p.y * S + S / 2;
            Color32 c32 = c;
            for (int x = cx - r; x <= cx + r; x++)
                for (int y = cy - r; y <= cy + r; y++)
                    if (Mathf.Abs(x - cx) + Mathf.Abs(y - cy) <= r && x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c32;
        }

        void Square(Vector2Int p, int r, Color c)
        {
            int cx = p.x * S + S / 2, cy = p.y * S + S / 2;
            Color32 c32 = c;
            for (int x = cx - r; x < cx + r; x++)
                for (int y = cy - r; y < cy + r; y++)
                    if (x >= 0 && y >= 0 && x < w && y < h) px[y * w + x] = c32;
        }

        void Line(Vector3 a, Vector3 b, Color c)
        {
            Color32 c32 = c;
            var pa = new Vector2((a.x + 0.5f) * S, (a.z + 0.5f) * S);
            var pb = new Vector2((b.x + 0.5f) * S, (b.z + 0.5f) * S);
            int steps = Mathf.CeilToInt((pb - pa).magnitude);
            for (int i = 0; i <= steps; i++)
            {
                // Clamp to the map so a view larger than the board traces its edge instead of vanishing.
                var p = Vector2.Lerp(pa, pb, steps == 0 ? 0 : i / (float)steps);
                int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1), y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                px[y * w + x] = c32;
            }
        }
    }
}
