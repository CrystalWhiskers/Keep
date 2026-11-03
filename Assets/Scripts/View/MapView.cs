using System;
using System.Collections.Generic;
using UnityEngine;

namespace Strategy
{
    /// <summary>A set of tiles drawn as one outline around its outer edge (move range, placement, targets).</summary>
    public class OutlineLayer
    {
        readonly Mesh mesh;
        readonly MeshRenderer renderer;
        readonly float width, height;

        public OutlineLayer(string name, Transform parent, float width, float height)
        {
            this.width = width;
            this.height = height;
            mesh = new Mesh { name = name };
            var go = MeshKit.Obj(name, parent, mesh, MeshKit.Trans, Color.white, Vector3.zero, Vector3.one);
            renderer = go.GetComponent<MeshRenderer>();
        }

        public void Set(ICollection<Vector2Int> tiles, Color color, bool eachTile = false)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var set = tiles as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(tiles);
            foreach (var p in set)
            {
                foreach (var d in GameModel.Dirs4)
                {
                    if (!eachTile && set.Contains(p + d)) continue;
                    var c = new Vector3(p.x, height + EntityView.GroundY(p), p.y) + new Vector3(d.x, 0, d.y) * 0.5f;
                    var along = new Vector3(d.y, 0, d.x) * 0.5f;
                    float inset = eachTile ? 0.06f : 0f;
                    c -= new Vector3(d.x, 0, d.y) * inset;
                    along *= eachTile ? 0.88f : 1f;
                    MeshKit.AddLine(v, t, c - along, c + along, width);
                }
            }
            MeshKit.Fill(mesh, v, t);
            MeshKit.SetColor(renderer, color);
        }

        public void Clear() => mesh.Clear();
    }

    /// <summary>
    /// The board: a floating island of earth with one coloured quad per tile on top, cliffs, rocks, pine groves,
    /// points of interest, and the highlight layers.
    /// </summary>
    public class MapView
    {
        readonly GameModel model;
        readonly Mesh tileMesh;
        readonly Color[] colors;
        readonly List<Vector2Int> cliffOwner = new List<Vector2Int>();
        readonly Dictionary<Vector2Int, Renderer[]> rocks = new Dictionary<Vector2Int, Renderer[]>();
        readonly Dictionary<Vector2Int, (Transform root, Renderer[] parts)> groves = new Dictionary<Vector2Int, (Transform, Renderer[])>();
        readonly List<PoiView> pois = new List<PoiView>();
        readonly List<Transform> drifters = new List<Transform>();
        readonly Transform treeRoot;
        int terrainVersion = -1;
        public readonly Transform Root;
        public readonly OutlineLayer Threat, Range, Targets, Place, Hover;
        readonly Transform brackets;
        readonly Renderer bracketRenderer;

        /// <summary>How far water sits below the land around it.</summary>
        const float WaterDrop = 0.1f;

        public MapView(GameModel model, Transform parent)
        {
            this.model = model;
            Root = new GameObject("Map").transform;
            Root.SetParent(parent, false);

            int w = model.W, h = model.H;
            BuildIsland(w, h);

            var heights = new int[w, h];
            foreach (var tile in model.AllTiles()) heights[tile.Pos.x, tile.Pos.y] = tile.Height;
            EntityView.Heights = heights;

            // Tile tops: one quad per tile, slightly inset so the ground shows through as grid lines.
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            const float inset = 0.035f;
            float half = 0.5f - inset;
            float Top(int x, int y) => heights[x, y] * EntityView.Level - (model.Tiles[x, y].Water ? WaterDrop : 0f);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    int i = v.Count;
                    float top = Top(x, y);
                    v.Add(new Vector3(x - half, top, y - half)); v.Add(new Vector3(x - half, top, y + half));
                    v.Add(new Vector3(x + half, top, y + half)); v.Add(new Vector3(x + half, top, y - half));
                    for (int k = 0; k < 4; k++) n.Add(Vector3.up);
                    t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }

            // Cliff faces wherever a tile stands above its neighbour (or the board edge). Coloured with their tile.
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    float ht = Top(x, y);
                    foreach (var d in GameModel.Dirs4)
                    {
                        int nx = x + d.x, ny = y + d.y;
                        float nh = nx >= 0 && ny >= 0 && nx < w && ny < h ? Top(nx, ny) : -0.16f;
                        if (nh >= ht - 0.001f) continue;
                        var normal = new Vector3(d.x, 0, d.y);
                        var c = new Vector3(x, 0, y) + normal * half;
                        var along = new Vector3(-d.y, 0, d.x) * half;
                        int i = v.Count;
                        v.Add(c - along + Vector3.up * nh); v.Add(c - along + Vector3.up * ht);
                        v.Add(c + along + Vector3.up * ht); v.Add(c + along + Vector3.up * nh);
                        for (int k = 0; k < 4; k++) n.Add(normal);
                        t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                        cliffOwner.Add(new Vector2Int(x, y));
                    }
                }
            tileMesh = new Mesh { name = "Tiles", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            tileMesh.SetVertices(v);
            tileMesh.SetNormals(n);
            colors = new Color[v.Count];
            tileMesh.colors = colors;
            tileMesh.SetTriangles(t, 0);
            tileMesh.RecalculateBounds();
            MeshKit.Obj("Tiles", Root, tileMesh, MeshKit.Flat, Color.white, new Vector3(0, 0.001f, 0), Vector3.one);

            foreach (var tile in model.AllTiles())
            {
                if (!tile.Rock) continue;
                var p = tile.Pos;
                var rockRoot = new GameObject("Rock").transform;
                rockRoot.SetParent(Root, false);
                rockRoot.localPosition = EntityView.Tile(p);
                float r = 0.5f + (p.x * 7 + p.y * 13) % 5 * 0.04f;
                var a = MeshKit.Obj("RockA", rockRoot, MeshKit.Box, MeshKit.Flat, Palette.Rock,
                    new Vector3(0, 0.17f, 0), new Vector3(0.78f, 0.34f, 0.78f), Quaternion.Euler(0, (p.x * 31 + p.y * 17) % 40 - 20, 0));
                var b = MeshKit.Obj("RockB", rockRoot, MeshKit.Cylinder(0.5f, 0.1f, 5), MeshKit.Flat, Palette.Rock,
                    new Vector3(-0.08f, 0.3f, 0.08f), new Vector3(0.5f, r * 0.9f, 0.5f));
                rocks[p] = new[] { a.GetComponent<Renderer>(), b.GetComponent<Renderer>() };
            }

            treeRoot = new GameObject("Trees").transform;
            treeRoot.SetParent(Root, false);
            SyncTerrain();

            foreach (var tile in model.AllTiles())
                if (tile.Poi != PoiType.None) pois.Add(new PoiView(tile, Root));

            Threat = new OutlineLayer("ThreatOutline", Root, 0.04f, 0.01f);
            Range = new OutlineLayer("RangeOutline", Root, 0.05f, 0.012f);
            Place = new OutlineLayer("PlaceOutline", Root, 0.045f, 0.014f);
            Targets = new OutlineLayer("TargetOutline", Root, 0.05f, 0.016f);
            Hover = new OutlineLayer("HoverOutline", Root, 0.035f, 0.018f);

            // Corner brackets around the selected tile, as in the mockup.
            brackets = new GameObject("Brackets").transform;
            brackets.SetParent(Root, false);
            var bm = new Mesh { name = "Brackets" };
            var bv = new List<Vector3>();
            var bt = new List<int>();
            const float e = 0.62f, len = 0.2f, bw = 0.055f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var corner = new Vector3(sx * e, 0, sz * e);
                    MeshKit.AddLine(bv, bt, corner, corner - new Vector3(sx * len, 0, 0), bw);
                    MeshKit.AddLine(bv, bt, corner, corner - new Vector3(0, 0, sz * len), bw);
                }
            MeshKit.Fill(bm, bv, bt);
            bracketRenderer = MeshKit.Obj("BracketMesh", brackets, bm, MeshKit.Trans, Palette.White, new Vector3(0, 0.02f, 0), Vector3.one)
                .GetComponent<Renderer>();
            brackets.gameObject.SetActive(false);
        }

        /// <summary>The board floats: a slab of earth over a jagged rock root, with a few loose stones drifting around it.</summary>
        void BuildIsland(int w, int h)
        {
            var mid = new Vector3((w - 1) / 2f, 0, (h - 1) / 2f);
            MeshKit.Obj("Ground", Root, MeshKit.Box, MeshKit.Flat, Palette.Slab, mid + new Vector3(0, -0.4f, 0), new Vector3(w + 0.1f, 0.5f, h + 0.1f));
            MeshKit.Obj("Earth", Root, MeshKit.Box, MeshKit.Flat, Palette.Earth, mid + new Vector3(0, -1.05f, 0), new Vector3(w + 0.3f, 0.8f, h + 0.3f));
            // An upside-down four-sided cone under the slab, plus a few smaller roots, for the floating-island look.
            var rnd = new System.Random(w * 31 + h);
            MeshKit.Obj("Root", Root, MeshKit.Cylinder(0.5f, 0f, 4), MeshKit.Flat, Color.Lerp(Palette.Earth, Palette.Slab, 0.35f),
                mid + new Vector3(0, -1.4f, 0), new Vector3(w * 1.38f, w * 0.55f, h * 1.38f), Quaternion.Euler(180, 45, 0));
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3 + (float)rnd.NextDouble() * 0.5f;
                var off = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * w * 0.32f;
                float s = 1.5f + (float)rnd.NextDouble() * 2f;
                MeshKit.Obj("RootB", Root, MeshKit.Cylinder(0.5f, 0f, 5), MeshKit.Flat, Color.Lerp(Palette.Earth, Palette.Slab, 0.5f),
                    mid + off + new Vector3(0, -1.3f, 0), new Vector3(s, s * 1.6f, s), Quaternion.Euler(180, i * 37, 0));
            }
            // Loose rocks floating beside and behind the island. The camera looks along +x+z, so anything on the near
            // side would hang in front of the board; keep them to the flanks and the far side.
            for (int i = 0; i < 7; i++)
            {
                float a = (-50f + (float)rnd.NextDouble() * 190f) * Mathf.Deg2Rad;
                float r = w * 0.85f + (float)rnd.NextDouble() * w * 0.3f;
                var pos = mid + new Vector3(Mathf.Cos(a) * r, -2f - (float)rnd.NextDouble() * 3f, Mathf.Sin(a) * r);
                float s = 0.4f + (float)rnd.NextDouble() * 0.7f;
                var stone = new GameObject("Drifter").transform;
                stone.SetParent(Root, false);
                stone.localPosition = pos;
                MeshKit.Obj("Top", stone, MeshKit.Box, MeshKit.Flat, Color.Lerp(Palette.ForestFloor, Palette.Slab, 0.3f),
                    Vector3.zero, new Vector3(s, s * 0.25f, s));
                MeshKit.Obj("Under", stone, MeshKit.Cylinder(0.5f, 0f, 4), MeshKit.Flat, Palette.Earth,
                    new Vector3(0, -s * 0.12f, 0), new Vector3(s * 1.3f, s * 1.1f, s * 1.3f), Quaternion.Euler(180, 45, 0));
                drifters.Add(stone);
            }
        }

        /// <summary>Plants or clears pine groves to match the model (forests can be grown or burnt during play).</summary>
        void SyncTerrain()
        {
            terrainVersion = model.TerrainVersion;
            foreach (var tile in model.AllTiles())
            {
                var p = tile.Pos;
                bool has = groves.ContainsKey(p);
                if (tile.Forest && !has) groves[p] = Grove(p);
                else if (!tile.Forest && has)
                {
                    UnityEngine.Object.Destroy(groves[p].root.gameObject);
                    groves.Remove(p);
                }
            }
        }

        (Transform, Renderer[]) Grove(Vector2Int p)
        {
            var root = new GameObject("Grove").transform;
            root.SetParent(treeRoot, false);
            root.localPosition = EntityView.Tile(p);
            var parts = new List<Renderer>();
            int seed = p.x * 73 + p.y * 151;
            var spots = new[] { new Vector3(-0.22f, 0, 0.2f), new Vector3(0.24f, 0, 0.14f), new Vector3(0.02f, 0, -0.24f) };
            for (int i = 0; i < spots.Length; i++)
            {
                float s = 0.75f + ((seed >> i) % 4) * 0.08f;
                var pos = spots[i] + new Vector3(((seed + i * 13) % 7 - 3) * 0.015f, 0, 0);
                parts.Add(MeshKit.Obj("Trunk", root, MeshKit.Cylinder(0.5f, 0.4f, 6), MeshKit.Flat, Palette.Earth,
                    pos, new Vector3(0.07f, 0.12f, 0.07f) * s).GetComponent<Renderer>());
                parts.Add(MeshKit.Obj("Low", root, MeshKit.Cylinder(0.5f, 0f, 7), MeshKit.Flat, Palette.Tree,
                    pos + Vector3.up * 0.1f * s, new Vector3(0.36f, 0.34f, 0.36f) * s).GetComponent<Renderer>());
                parts.Add(MeshKit.Obj("High", root, MeshKit.Cylinder(0.5f, 0f, 7), MeshKit.Flat, Palette.Tree,
                    pos + Vector3.up * 0.28f * s, new Vector3(0.27f, 0.3f, 0.27f) * s).GetComponent<Renderer>());
            }
            return (root, parts.ToArray());
        }

        public void SetColors(Func<Tile, Color> colorOf)
        {
            int i = 0;
            var top = new Color[model.W, model.H];
            for (int x = 0; x < model.W; x++)
                for (int y = 0; y < model.H; y++)
                {
                    var tile = model.Tiles[x, y];
                    var raw = colorOf(tile);
                    // Higher ground reads a touch lighter, once it has been seen.
                    if (tile.Explored && tile.Height > 0) raw = Color.Lerp(raw, Color.white, 0.07f * tile.Height);
                    top[x, y] = raw;
                    var c = MeshKit.Lin(raw);
                    colors[i++] = c; colors[i++] = c; colors[i++] = c; colors[i++] = c;
                }
            foreach (var p in cliffOwner)
            {
                var c = MeshKit.Lin(Color.Lerp(top[p.x, p.y], Palette.Slab, 0.45f));
                colors[i++] = c; colors[i++] = c; colors[i++] = c; colors[i++] = c;
            }
            tileMesh.colors = colors;

            foreach (var kv in rocks)
            {
                var tile = model.T(kv.Key);
                foreach (var r in kv.Value)
                {
                    r.enabled = tile.Explored;
                    MeshKit.SetColor(r, tile.Explored && model.SeenBy(Faction.Player, kv.Key) ? Palette.Rock : Color.Lerp(Palette.Rock, Palette.Explored, 0.6f));
                }
            }
            foreach (var kv in groves)
            {
                var tile = model.T(kv.Key);
                bool lit = tile.Visible || Lit(kv.Key);
                kv.Value.root.gameObject.SetActive(tile.Explored);
                foreach (var r in kv.Value.parts)
                {
                    bool trunk = r.name == "Trunk";
                    var c = trunk ? Palette.Earth : Palette.Tree;
                    MeshKit.SetColor(r, lit ? c : Color.Lerp(c, Palette.Explored, 0.55f));
                }
            }
            foreach (var poi in pois) poi.Sync();
        }

        /// <summary>A forest next to something we can see is drawn lit, even though squads inside it stay hidden.</summary>
        bool Lit(Vector2Int p)
        {
            foreach (var d in GameModel.Dirs8)
                if (model.In(p + d) && model.T(p + d).Visible) return true;
            return false;
        }

        public void SetBrackets(Vector2Int? tile, Color color)
        {
            brackets.gameObject.SetActive(tile.HasValue);
            if (!tile.HasValue) return;
            brackets.localPosition = EntityView.Tile(tile.Value);
            MeshKit.SetColor(bracketRenderer, color);
        }

        public void Tick()
        {
            if (terrainVersion != model.TerrainVersion) SyncTerrain();
            for (int i = 0; i < drifters.Count; i++)
            {
                var d = drifters[i];
                var p = d.localPosition;
                p.y += Mathf.Sin(Time.time * 0.6f + i * 1.7f) * 0.0015f;
                d.localPosition = p;
                d.localRotation = Quaternion.Euler(0, Time.time * (4f + i), 0);
            }
            foreach (var poi in pois) poi.Tick();
            if (!brackets.gameObject.activeSelf) return;
            float s = 1f + Mathf.Sin(Time.time * 4f) * 0.04f;
            brackets.localScale = new Vector3(s, 1, s);
        }
    }

    /// <summary>
    /// A point of interest on the board: a mine, library, shrine, outpost or spring. Its banner and trim take the
    /// colour of whoever holds it (grey when unclaimed).
    /// </summary>
    public class PoiView
    {
        readonly Tile tile;
        readonly Transform root, spin;
        readonly List<(Renderer r, Color baseColor, bool owned)> parts = new List<(Renderer, Color, bool)>();

        public PoiView(Tile tile, Transform parent)
        {
            this.tile = tile;
            root = new GameObject(tile.Poi.ToString()).transform;
            root.SetParent(parent, false);
            root.localPosition = EntityView.Tile(tile.Pos);
            var stone = Color.Lerp(Palette.Rock, Palette.Earth, 0.3f);
            switch (tile.Poi)
            {
                case PoiType.GoldMine:
                    Add(MeshKit.Cylinder(0.5f, 0.15f, 6), new Vector3(0, 0, 0.05f), new Vector3(0.8f, 0.36f, 0.7f), stone);
                    Add(MeshKit.Box, new Vector3(0, 0.12f, -0.27f), new Vector3(0.26f, 0.24f, 0.06f), Color.Lerp(Palette.Slab, Color.black, 0.4f));
                    Add(MeshKit.Box, new Vector3(0, 0.26f, -0.29f), new Vector3(0.34f, 0.05f, 0.06f), Palette.Earth, true);
                    Add(MeshKit.Sphere, new Vector3(0.24f, 0.05f, -0.3f), Vector3.one * 0.12f, Palette.Gold);
                    Add(MeshKit.Sphere, new Vector3(0.33f, 0.04f, -0.2f), Vector3.one * 0.09f, Palette.Gold);
                    Banner(new Vector3(-0.3f, 0, 0.25f));
                    break;
                case PoiType.Library:
                    // A ruined arch with a book of light floating inside.
                    Add(MeshKit.Box, new Vector3(-0.26f, 0.25f, 0), new Vector3(0.14f, 0.5f, 0.14f), stone);
                    Add(MeshKit.Box, new Vector3(0.26f, 0.2f, 0), new Vector3(0.14f, 0.4f, 0.14f), stone);
                    Add(MeshKit.Box, new Vector3(-0.08f, 0.52f, 0), new Vector3(0.5f, 0.08f, 0.16f), stone, true);
                    Add(MeshKit.Box, new Vector3(0, 0.03f, 0), new Vector3(0.7f, 0.06f, 0.36f), Color.Lerp(stone, Palette.Slab, 0.3f));
                    spin = Add(MeshKit.Box, new Vector3(0, 0.3f, 0), new Vector3(0.18f, 0.03f, 0.13f), Palette.Jade).transform;
                    break;
                case PoiType.Shrine:
                    Add(MeshKit.Cylinder(0.5f, 0.42f, 8), Vector3.zero, new Vector3(0.6f, 0.08f, 0.6f), stone);
                    spin = Add(MeshKit.Cylinder(0.5f, 0f, 4), new Vector3(0, 0.36f, 0), new Vector3(0.22f, 0.36f, 0.22f), Palette.Lilac).transform;
                    Add(MeshKit.Cylinder(0.5f, 0f, 4), new Vector3(0, 0.37f, 0), new Vector3(0.22f, 0.26f, 0.22f), Palette.Lilac, false,
                        Quaternion.Euler(180, 0, 0));
                    foreach (var a in new[] { 0f, 120f, 240f })
                    {
                        var d = Quaternion.Euler(0, a, 0) * new Vector3(0.24f, 0, 0);
                        Add(MeshKit.Box, d + new Vector3(0, 0.13f, 0), new Vector3(0.07f, 0.18f, 0.07f), stone, true);
                    }
                    break;
                case PoiType.Outpost:
                    for (int i = 0; i < 8; i++)
                    {
                        var d = Quaternion.Euler(0, i * 45f, 0) * new Vector3(0.33f, 0, 0);
                        if (i == 6) continue; // a gap for the gate
                        Add(MeshKit.Cylinder(0.5f, 0.25f, 5), d, new Vector3(0.09f, 0.3f, 0.09f), Palette.Earth);
                    }
                    Banner(Vector3.zero, 0.75f);
                    break;
                case PoiType.Spring:
                    Add(MeshKit.Cylinder(0.5f, 0.5f, 10), Vector3.zero, new Vector3(0.62f, 0.05f, 0.62f), stone);
                    spin = Add(MeshKit.Disc, new Vector3(0, 0.055f, 0), new Vector3(0.5f, 1, 0.5f), Palette.Cyan).transform;
                    for (int i = 0; i < 6; i++)
                    {
                        var d = Quaternion.Euler(0, i * 60f + 15f, 0) * new Vector3(0.31f, 0, 0);
                        Add(MeshKit.Sphere, d + new Vector3(0, 0.05f, 0), new Vector3(0.12f, 0.09f, 0.12f), stone);
                    }
                    break;
            }
        }

        void Banner(Vector3 at, float height = 0.6f)
        {
            Add(MeshKit.Box, at + new Vector3(0, height / 2, 0), new Vector3(0.03f, height, 0.03f), Palette.Earth);
            Add(MeshKit.Box, at + new Vector3(0.11f, height - 0.1f, 0), new Vector3(0.2f, 0.14f, 0.015f), Palette.Neutral, true);
        }

        Renderer Add(Mesh mesh, Vector3 pos, Vector3 scale, Color c, bool owned = false, Quaternion? rot = null)
        {
            var r = MeshKit.Obj("Part", root, mesh, MeshKit.Flat, c, pos, scale, rot).GetComponent<Renderer>();
            parts.Add((r, c, owned));
            return r;
        }

        public void Sync()
        {
            root.gameObject.SetActive(tile.Explored);
            if (!tile.Explored) return;
            var owner = tile.Owner.HasValue ? Palette.Of(tile.Owner.Value) : Palette.Neutral;
            foreach (var (r, baseColor, owned) in parts)
            {
                var c = owned ? owner : baseColor;
                MeshKit.SetColor(r, tile.Visible ? c : Color.Lerp(c, Palette.Explored, 0.55f));
            }
        }

        public void Tick()
        {
            if (spin == null || !root.gameObject.activeSelf) return;
            if (tile.Poi == PoiType.Spring)
            {
                float s = 0.48f + Mathf.Sin(Time.time * 2f) * 0.03f;
                spin.localScale = new Vector3(s, 1, s);
                return;
            }
            spin.localRotation = Quaternion.Euler(0, Time.time * 40f, 0);
            var p = spin.localPosition;
            p.y = (tile.Poi == PoiType.Library ? 0.3f : 0.36f) + Mathf.Sin(Time.time * 1.8f) * 0.03f;
            spin.localPosition = p;
        }
    }
}
