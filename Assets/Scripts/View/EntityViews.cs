using System.Collections.Generic;
using UnityEngine;

namespace Strategy
{
    /// <summary>Scene object for a squad or building. Parts tagged with a lighten amount so tinting keeps accents readable.</summary>
    public class EntityView
    {
        public readonly Entity Entity;
        public readonly Transform Root;
        public HudBadge Badge;
        protected readonly Material mat;
        protected readonly float alpha;
        readonly List<(Renderer r, float lighten)> parts = new List<(Renderer, float)>();
        Color tint;

        protected EntityView(Entity e, Transform parent, bool ghost)
        {
            Entity = e;
            mat = ghost ? MeshKit.Trans : MeshKit.Flat;
            alpha = ghost ? 0.55f : 1f;
            Root = new GameObject(e != null ? e.Name : "Ghost").transform;
            Root.SetParent(parent, false);
            Root.localRotation = Quaternion.Euler(0, CameraRig.Yaw, 0);
            if (e != null) Root.localPosition = Tile(e.Pos);
        }

        /// <summary>World height of one elevation step.</summary>
        public const float Level = 0.3f;
        /// <summary>Tile heights of the current map, set by <see cref="MapView"/>, so anything can find the ground.</summary>
        public static int[,] Heights;

        public static float GroundY(Vector2Int p) =>
            Heights != null && p.x >= 0 && p.y >= 0 && p.x < Heights.GetLength(0) && p.y < Heights.GetLength(1)
                ? Heights[p.x, p.y] * Level : 0f;

        public static Vector3 Tile(Vector2Int p) => new Vector3(p.x, GroundY(p), p.y);

        protected Transform Part(Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, float lighten = 0f, Quaternion? rot = null)
        {
            var go = MeshKit.Obj("Part", parent, mesh, mat, Color.white, pos, scale, rot);
            parts.Add((go.GetComponent<Renderer>(), lighten));
            return go.transform;
        }

        protected void Shadow(float w, float d, Color c)
        {
            MeshKit.Obj("Shadow", Root, MeshKit.Disc, MeshKit.Trans, c, new Vector3(0, 0.01f, 0), new Vector3(w, 1, d));
        }

        public void SetTint(Color c)
        {
            if (c == tint) return;
            tint = c;
            foreach (var (r, lighten) in parts)
            {
                var col = lighten >= 0 ? Color.Lerp(c, Color.white, lighten) : Color.Lerp(c, Color.black, -lighten);
                col.a = alpha;
                MeshKit.SetColor(r, col);
            }
        }

        public void SetVisible(bool v)
        {
            if (Root.gameObject.activeSelf != v) Root.gameObject.SetActive(v);
        }

        public Vector3 BadgeAnchor => Root.position + Vector3.up * BadgeHeight;
        protected virtual float BadgeHeight => 0.85f;

        public virtual void Tick() { }

        public void Destroy()
        {
            if (Badge != null) Badge.Destroy();
            Object.Destroy(Root.gameObject);
        }
    }

    public class UnitView : EntityView
    {
        public readonly List<Transform> Figures = new List<Transform>();
        public readonly Transform Body;

        public UnitView(Unit u, Transform parent, bool ghost = false, UnitType? ghostType = null, int ghostCount = 4)
            : base(u, parent, ghost)
        {
            var type = u?.Type ?? ghostType ?? UnitType.Minions;
            Body = new GameObject("Body").transform;
            Body.SetParent(Root, false);
            if (!ghost) Shadow(type == UnitType.Minions ? 0.85f : 0.6f, type == UnitType.Minions ? 0.7f : 0.5f, new Color(0, 0, 0, 0.35f));

            if (type == UnitType.Minions)
            {
                var offsets = new[]
                {
                    new Vector3(-0.15f, 0, 0.14f), new Vector3(0.15f, 0, 0.14f),
                    new Vector3(-0.17f, 0, -0.14f), new Vector3(0.17f, 0, -0.14f),
                };
                int count = u?.MaxHp ?? ghostCount;
                for (int i = 0; i < count && i < offsets.Length; i++) Figures.Add(Figure(offsets[i], 0.72f));
            }
            else if (type == UnitType.Giant)
            {
                var f = Giant();
                Figures.Add(f);
            }
            else if (type == UnitType.Wyvern)
            {
                var f = Wyvern();
                Figures.Add(f);
            }
            else
            {
                float s = type == UnitType.Knight ? 1.12f : type == UnitType.Ninja ? 0.92f : 1f;
                var f = Figure(Vector3.zero, s);
                Figures.Add(f);
                Accessory(type, f);
            }
            badgeHeight = type == UnitType.Giant ? 1.35f : type == UnitType.Wyvern ? 1.2f : 0.85f;
            hover = type == UnitType.Wyvern;
            SetTint(ghost ? Palette.Cyan : Palette.Of(u.Faction));
        }

        float badgeHeight = 0.85f;
        bool hover;
        protected override float BadgeHeight => badgeHeight;

        /// <summary>A hulking hill giant with a stone club: twice the size of a soldier.</summary>
        Transform Giant()
        {
            var root = new GameObject("Giant").transform;
            root.SetParent(Body, false);
            Part(root, MeshKit.Cylinder(0.5f, 0.42f, 10), new Vector3(0, 0.12f, 0), new Vector3(0.48f, 0.5f, 0.42f));
            Part(root, MeshKit.Cylinder(0.3f, 0.5f, 10), new Vector3(0, 0.5f, 0), new Vector3(0.62f, 0.26f, 0.5f), 0.08f);
            Part(root, MeshKit.Sphere, new Vector3(0, 0.86f, -0.03f), Vector3.one * 0.26f, 0.12f);
            // Brow ridge and a little topknot.
            Part(root, MeshKit.Box, new Vector3(0, 0.9f, -0.13f), new Vector3(0.22f, 0.04f, 0.05f), -0.3f);
            Part(root, MeshKit.Cylinder(0.5f, 0f, 5), new Vector3(0, 0.97f, 0.04f), new Vector3(0.08f, 0.12f, 0.08f), -0.3f);
            // Legs.
            Part(root, MeshKit.Box, new Vector3(-0.12f, 0.05f, 0), new Vector3(0.14f, 0.16f, 0.16f), -0.25f);
            Part(root, MeshKit.Box, new Vector3(0.12f, 0.05f, 0), new Vector3(0.14f, 0.16f, 0.16f), -0.25f);
            // Arms and the club.
            Part(root, MeshKit.Box, new Vector3(-0.36f, 0.45f, 0), new Vector3(0.12f, 0.36f, 0.12f), 0.05f, Quaternion.Euler(0, 0, -12));
            Part(root, MeshKit.Box, new Vector3(0.37f, 0.48f, -0.06f), new Vector3(0.12f, 0.32f, 0.12f), 0.05f, Quaternion.Euler(30, 0, 10));
            Part(root, MeshKit.Cylinder(0.35f, 0.6f, 7), new Vector3(0.42f, 0.55f, -0.18f), new Vector3(0.16f, 0.5f, 0.16f), -0.35f,
                Quaternion.Euler(-35, 0, -10));
            return root;
        }

        /// <summary>A wyvern with a small rider: long neck, spread wings, tail. It hovers above the ground.</summary>
        Transform Wyvern()
        {
            var root = new GameObject("Wyvern").transform;
            root.SetParent(Body, false);
            root.localPosition = new Vector3(0, 0.22f, 0);
            Part(root, MeshKit.Sphere, new Vector3(0, 0.12f, 0), new Vector3(0.3f, 0.24f, 0.46f), -0.05f);
            Part(root, MeshKit.Box, new Vector3(0, 0.3f, -0.26f), new Vector3(0.08f, 0.32f, 0.08f), -0.05f, Quaternion.Euler(-30, 0, 0));
            Part(root, MeshKit.Box, new Vector3(0, 0.46f, -0.38f), new Vector3(0.1f, 0.09f, 0.2f), 0.1f);
            Part(root, MeshKit.Cylinder(0.5f, 0f, 4), new Vector3(0.04f, 0.5f, -0.33f), new Vector3(0.03f, 0.1f, 0.03f), 0.5f, Quaternion.Euler(-40, 0, 0));
            Part(root, MeshKit.Cylinder(0.5f, 0f, 4), new Vector3(-0.04f, 0.5f, -0.33f), new Vector3(0.03f, 0.1f, 0.03f), 0.5f, Quaternion.Euler(-40, 0, 0));
            Part(root, MeshKit.Cylinder(0.5f, 0f, 5), new Vector3(0, 0.1f, 0.3f), new Vector3(0.1f, 0.4f, 0.1f), -0.1f, Quaternion.Euler(100, 0, 0));
            // Wings hinge at the shoulders.
            leftWing = Hinge(root, new Vector3(-0.08f, 0.22f, 0.02f));
            rightWing = Hinge(root, new Vector3(0.08f, 0.22f, 0.02f));
            Part(leftWing, MeshKit.Box, new Vector3(-0.24f, 0, 0), new Vector3(0.48f, 0.02f, 0.32f), 0.25f);
            Part(leftWing, MeshKit.Box, new Vector3(-0.36f, 0, 0.12f), new Vector3(0.24f, 0.02f, 0.16f), 0.15f, Quaternion.Euler(0, -20, 0));
            Part(rightWing, MeshKit.Box, new Vector3(0.24f, 0, 0), new Vector3(0.48f, 0.02f, 0.32f), 0.25f);
            Part(rightWing, MeshKit.Box, new Vector3(0.36f, 0, 0.12f), new Vector3(0.24f, 0.02f, 0.16f), 0.15f, Quaternion.Euler(0, 20, 0));
            // The rider.
            Part(root, MeshKit.Cylinder(0.5f, 0.3f), new Vector3(0, 0.22f, 0.02f), new Vector3(0.16f, 0.16f, 0.16f), 0.3f);
            Part(root, MeshKit.Sphere, new Vector3(0, 0.44f, 0.02f), Vector3.one * 0.11f, 0.3f);
            return root;
        }

        Transform leftWing, rightWing;

        static Transform Hinge(Transform parent, Vector3 at)
        {
            var t = new GameObject("Hinge").transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            return t;
        }

        /// <summary>A short cloak hanging down the back, in a darker shade of the squad's colour.</summary>
        void Cape(Transform f, float length = 0.28f)
        {
            Part(f, MeshKit.Box, new Vector3(0, 0.27f - length / 2 + 0.02f, 0.13f), new Vector3(0.24f, length, 0.025f), -0.4f, Quaternion.Euler(-8, 0, 0));
        }

        Transform Figure(Vector3 offset, float s)
        {
            var root = new GameObject("Figure").transform;
            root.SetParent(Body, false);
            root.localPosition = offset;
            root.localScale = Vector3.one * s;
            Part(root, MeshKit.Cylinder(0.5f, 0.3f), Vector3.zero, new Vector3(0.28f, 0.24f, 0.28f));
            Part(root, MeshKit.Sphere, new Vector3(0, 0.34f, 0), Vector3.one * 0.19f);
            return root;
        }

        void Accessory(UnitType type, Transform f)
        {
            switch (type)
            {
                case UnitType.Shield:
                    Part(f, MeshKit.Box, new Vector3(-0.13f, 0.17f, -0.12f), new Vector3(0.22f, 0.26f, 0.05f), 0.35f);
                    break;
                case UnitType.Sword:
                    Cape(f);
                    Part(f, MeshKit.Box, new Vector3(0.18f, 0.26f, -0.04f), new Vector3(0.04f, 0.38f, 0.04f), 0.55f);
                    Part(f, MeshKit.Box, new Vector3(0.18f, 0.1f, -0.04f), new Vector3(0.12f, 0.03f, 0.04f), 0.2f);
                    break;
                case UnitType.Bow:
                    Part(f, MeshKit.Box, new Vector3(0.19f, 0.22f, 0f), new Vector3(0.035f, 0.42f, 0.035f), 0.45f,
                        Quaternion.Euler(0, 0, -8f));
                    Part(f, MeshKit.Box, new Vector3(0.24f, 0.22f, 0f), new Vector3(0.012f, 0.4f, 0.012f), 0.8f);
                    break;
                case UnitType.Knight:
                    Cape(f, 0.34f);
                    Part(f, MeshKit.Cylinder(0.5f, 0f), new Vector3(0, 0.4f, 0), new Vector3(0.21f, 0.16f, 0.21f), 0.35f);
                    Part(f, MeshKit.Box, new Vector3(0, 0.55f, 0.02f), new Vector3(0.025f, 0.08f, 0.12f), 0.6f);
                    Part(f, MeshKit.Box, new Vector3(-0.14f, 0.16f, -0.11f), new Vector3(0.2f, 0.22f, 0.05f), 0.25f);
                    Part(f, MeshKit.Box, new Vector3(0.18f, 0.26f, -0.04f), new Vector3(0.04f, 0.36f, 0.04f), 0.55f);
                    break;
                case UnitType.Spear:
                    Part(f, MeshKit.Box, new Vector3(0.17f, 0.3f, 0f), new Vector3(0.03f, 0.62f, 0.03f), 0.3f);
                    Part(f, MeshKit.Cylinder(0.5f, 0f, 6), new Vector3(0.17f, 0.6f, 0f), new Vector3(0.07f, 0.13f, 0.07f), 0.6f);
                    break;
                case UnitType.Healer:
                    Cape(f, 0.32f);
                    Part(f, MeshKit.Cylinder(0.5f, 0.15f), new Vector3(0, 0.38f, 0.01f), new Vector3(0.22f, 0.14f, 0.22f), 0.45f);
                    Part(f, MeshKit.Box, new Vector3(0, 0.6f, 0), new Vector3(0.16f, 0.05f, 0.05f), 0.6f);
                    Part(f, MeshKit.Box, new Vector3(0, 0.6f, 0), new Vector3(0.05f, 0.16f, 0.05f), 0.6f);
                    break;
                case UnitType.Twinblade:
                    Part(f, MeshKit.Box, new Vector3(0.18f, 0.24f, -0.03f), new Vector3(0.035f, 0.32f, 0.035f), 0.55f,
                        Quaternion.Euler(0, 0, -14f));
                    Part(f, MeshKit.Box, new Vector3(-0.18f, 0.24f, -0.03f), new Vector3(0.035f, 0.32f, 0.035f), 0.55f,
                        Quaternion.Euler(0, 0, 14f));
                    break;
                case UnitType.Crossbow:
                    // Stock held level with a bow limb across the front.
                    Part(f, MeshKit.Box, new Vector3(0.16f, 0.22f, -0.12f), new Vector3(0.05f, 0.05f, 0.3f), 0.35f);
                    Part(f, MeshKit.Box, new Vector3(0.16f, 0.22f, -0.25f), new Vector3(0.28f, 0.035f, 0.04f), 0.6f);
                    break;
                case UnitType.Rider:
                    // A low mount under the figure, with a head out front.
                    Part(f, MeshKit.Box, new Vector3(0, 0.02f, 0), new Vector3(0.26f, 0.16f, 0.5f), -0.15f);
                    Part(f, MeshKit.Box, new Vector3(0, 0.14f, -0.27f), new Vector3(0.12f, 0.2f, 0.12f), -0.15f);
                    Part(f, MeshKit.Box, new Vector3(0.18f, 0.32f, -0.08f), new Vector3(0.03f, 0.5f, 0.03f), 0.45f,
                        Quaternion.Euler(-25f, 0, 0));
                    break;
                case UnitType.Pyromancer:
                    // Staff with a glowing tip and a pointed hood.
                    Part(f, MeshKit.Box, new Vector3(0.18f, 0.3f, 0f), new Vector3(0.03f, 0.58f, 0.03f), 0.25f);
                    Part(f, MeshKit.Sphere, new Vector3(0.18f, 0.62f, 0f), Vector3.one * 0.09f, 0.75f);
                    Part(f, MeshKit.Cylinder(0.5f, 0f), new Vector3(0, 0.44f, 0), new Vector3(0.2f, 0.2f, 0.2f), -0.2f);
                    break;
                case UnitType.Ninja:
                    // Dark wrap, a pale eye-slit, and two blades crossed on the back.
                    Part(f, MeshKit.Sphere, new Vector3(0, 0.35f, 0), Vector3.one * 0.205f, -0.55f);
                    Part(f, MeshKit.Box, new Vector3(0, 0.37f, -0.09f), new Vector3(0.14f, 0.03f, 0.03f), 0.75f);
                    Part(f, MeshKit.Box, new Vector3(0, 0.25f, 0.12f), new Vector3(0.03f, 0.42f, 0.03f), 0.55f, Quaternion.Euler(0, 0, 35));
                    Part(f, MeshKit.Box, new Vector3(0, 0.25f, 0.12f), new Vector3(0.03f, 0.42f, 0.03f), 0.55f, Quaternion.Euler(0, 0, -35));
                    Part(f, MeshKit.Box, new Vector3(0.1f, 0.42f, 0.08f), new Vector3(0.03f, 0.03f, 0.18f), -0.4f, Quaternion.Euler(20, 0, 0));
                    break;
                case UnitType.Druid:
                    // Antlered hood and a staff topped with a leaf bud.
                    Part(f, MeshKit.Cylinder(0.5f, 0.1f), new Vector3(0, 0.4f, 0), new Vector3(0.22f, 0.14f, 0.22f), -0.3f);
                    Part(f, MeshKit.Box, new Vector3(-0.08f, 0.56f, 0), new Vector3(0.025f, 0.18f, 0.025f), 0.35f, Quaternion.Euler(0, 0, 30));
                    Part(f, MeshKit.Box, new Vector3(0.08f, 0.56f, 0), new Vector3(0.025f, 0.18f, 0.025f), 0.35f, Quaternion.Euler(0, 0, -30));
                    Part(f, MeshKit.Box, new Vector3(-0.13f, 0.62f, 0), new Vector3(0.02f, 0.08f, 0.02f), 0.35f, Quaternion.Euler(0, 0, -20));
                    Part(f, MeshKit.Box, new Vector3(0.13f, 0.62f, 0), new Vector3(0.02f, 0.08f, 0.02f), 0.35f, Quaternion.Euler(0, 0, 20));
                    Part(f, MeshKit.Box, new Vector3(-0.19f, 0.28f, 0f), new Vector3(0.03f, 0.56f, 0.03f), -0.35f, Quaternion.Euler(0, 0, 6));
                    Part(f, MeshKit.Sphere, new Vector3(-0.21f, 0.58f, 0f), new Vector3(0.1f, 0.14f, 0.1f), 0.65f);
                    break;
            }
        }

        /// <summary>Minion squads hide figures as they take losses.</summary>
        public void SyncCount(int count)
        {
            if (Figures.Count <= 1) return;
            for (int i = 0; i < Figures.Count; i++) Figures[i].gameObject.SetActive(i < count);
        }

        public override void Tick()
        {
            // A gentle idle bob so the board feels alive.
            if (Entity == null) return;
            float t = Time.time * 2.2f + Entity.Id;
            if (hover)
            {
                // Wyverns bob higher and beat their wings.
                Body.localPosition = new Vector3(0, Mathf.Sin(t * 0.8f) * 0.05f, 0);
                float flap = Mathf.Sin(t * 3f) * 25f;
                leftWing.localRotation = Quaternion.Euler(0, 0, flap);
                rightWing.localRotation = Quaternion.Euler(0, 0, -flap);
                return;
            }
            Body.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(t)) * 0.015f, 0);
        }
    }

    public class BuildingView : EntityView
    {
        Transform orb;
        Vector3 orbHome;

        public BuildingView(Building b, Transform parent, bool ghost = false, BuildingType? ghostType = null)
            : base(b, parent, ghost)
        {
            var type = b?.Type ?? ghostType ?? BuildingType.Wall;
            if (!ghost) Shadow(0.95f, 0.95f, new Color(0, 0, 0, 0.3f));
            var r = Root;
            switch (type)
            {
                case BuildingType.Keep:
                    Part(r, MeshKit.Box, new Vector3(0, 0.12f, 0), new Vector3(0.76f, 0.24f, 0.76f), 0.0f);
                    foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                    {
                        Part(r, MeshKit.Box, new Vector3(c.x * 0.29f, 0.26f, c.y * 0.29f), new Vector3(0.2f, 0.52f, 0.2f), 0.12f);
                        Part(r, MeshKit.Box, new Vector3(c.x * 0.29f, 0.56f, c.y * 0.29f), new Vector3(0.26f, 0.08f, 0.26f), 0.25f);
                    }
                    Part(r, MeshKit.Box, new Vector3(0, 0.3f, 0.05f), new Vector3(0.4f, 0.4f, 0.4f), 0.05f);
                    Part(r, MeshKit.Box, new Vector3(0, 0.12f, -0.39f), new Vector3(0.16f, 0.2f, 0.02f), -0.6f);
                    break;
                case BuildingType.Barracks:
                    Part(r, MeshKit.Box, new Vector3(0, 0.15f, 0), new Vector3(0.74f, 0.3f, 0.6f));
                    Part(r, MeshKit.Box, new Vector3(0, 0.36f, 0), new Vector3(0.6f, 0.14f, 0.46f), 0.25f);
                    Part(r, MeshKit.Box, new Vector3(0, 0.1f, -0.31f), new Vector3(0.16f, 0.2f, 0.02f), -0.6f);
                    Part(r, MeshKit.Box, new Vector3(0.28f, 0.58f, 0.18f), new Vector3(0.02f, 0.3f, 0.02f), 0.5f);
                    Part(r, MeshKit.Box, new Vector3(0.34f, 0.66f, 0.18f), new Vector3(0.12f, 0.08f, 0.01f), 0.5f);
                    break;
                case BuildingType.ManaWell:
                    Part(r, MeshKit.Cylinder(0.5f, 0.46f, 18), Vector3.zero, new Vector3(0.66f, 0.2f, 0.66f));
                    Part(r, MeshKit.Cylinder(0.5f, 0.5f, 18), new Vector3(0, 0.2f, 0), new Vector3(0.5f, 0.01f, 0.5f), -0.5f);
                    orb = Part(r, MeshKit.Sphere, new Vector3(0, 0.55f, 0), Vector3.one * 0.22f, 0.5f);
                    break;
                case BuildingType.Watchtower:
                    Part(r, MeshKit.Cylinder(0.5f, 0.38f, 12), Vector3.zero, new Vector3(0.34f, 0.72f, 0.34f));
                    Part(r, MeshKit.Box, new Vector3(0, 0.78f, 0), new Vector3(0.42f, 0.14f, 0.42f), 0.2f);
                    Part(r, MeshKit.Cylinder(0.5f, 0f, 4), new Vector3(0, 0.85f, 0), new Vector3(0.5f, 0.24f, 0.5f), 0.35f,
                        Quaternion.Euler(0, 45, 0));
                    break;
                case BuildingType.Forge:
                    // Smithy with a chimney, an anvil out front and a glowing hearth.
                    Part(r, MeshKit.Box, new Vector3(0, 0.16f, 0.06f), new Vector3(0.66f, 0.32f, 0.5f));
                    Part(r, MeshKit.Cylinder(0.5f, 0f, 4), new Vector3(0, 0.32f, 0.06f), new Vector3(0.84f, 0.2f, 0.66f), 0.2f, Quaternion.Euler(0, 45, 0));
                    Part(r, MeshKit.Box, new Vector3(0.2f, 0.45f, 0.2f), new Vector3(0.14f, 0.36f, 0.14f), -0.2f);
                    orb = Part(r, MeshKit.Sphere, new Vector3(0.2f, 0.68f, 0.2f), Vector3.one * 0.1f, 0.85f);
                    Part(r, MeshKit.Box, new Vector3(-0.14f, 0.12f, -0.3f), new Vector3(0.22f, 0.07f, 0.1f), -0.45f);
                    Part(r, MeshKit.Box, new Vector3(-0.14f, 0.05f, -0.3f), new Vector3(0.08f, 0.1f, 0.06f), -0.45f);
                    Part(r, MeshKit.Box, new Vector3(0.1f, 0.1f, -0.2f), new Vector3(0.18f, 0.16f, 0.02f), 0.7f);
                    break;
                case BuildingType.Academy:
                    // A slim tower with a domed observatory and a floating tome.
                    Part(r, MeshKit.Box, new Vector3(0, 0.13f, 0), new Vector3(0.62f, 0.26f, 0.62f));
                    Part(r, MeshKit.Cylinder(0.5f, 0.42f, 10), new Vector3(0, 0.26f, 0), new Vector3(0.34f, 0.5f, 0.34f), 0.1f);
                    Part(r, MeshKit.Sphere, new Vector3(0, 0.76f, 0), new Vector3(0.34f, 0.26f, 0.34f), 0.3f);
                    Part(r, MeshKit.Cylinder(0.5f, 0f, 6), new Vector3(0, 0.86f, 0), new Vector3(0.05f, 0.16f, 0.05f), 0.5f);
                    orb = Part(r, MeshKit.Box, new Vector3(0, 0.55f, 0), new Vector3(0.18f, 0.035f, 0.13f), 0.8f);
                    break;
                case BuildingType.Wall:
                    Part(r, MeshKit.Box, new Vector3(0, 0.17f, 0), new Vector3(0.86f, 0.34f, 0.86f));
                    for (int i = -1; i <= 1; i++)
                    {
                        Part(r, MeshKit.Box, new Vector3(i * 0.3f, 0.39f, -0.33f), new Vector3(0.16f, 0.1f, 0.16f), 0.2f);
                        Part(r, MeshKit.Box, new Vector3(-0.33f, 0.39f, i * 0.3f), new Vector3(0.16f, 0.1f, 0.16f), 0.2f);
                    }
                    break;
            }
            if (orb != null) orbHome = orb.localPosition;
            SetTint(ghost ? Palette.Cyan : Palette.Of(b.Faction));
        }

        protected override float BadgeHeight => 1.05f;

        public override void Tick()
        {
            if (orb != null) orb.localPosition = orbHome + new Vector3(0, Mathf.Sin(Time.time * 2f) * 0.04f, 0);
        }
    }

    /// <summary>Gold sparkle floating over its tile.</summary>
    public class PickupView
    {
        public readonly Tile Tile;
        readonly Transform root, star;
        readonly Renderer starR, shadowR;

        public PickupView(Tile tile, Transform parent)
        {
            Tile = tile;
            root = new GameObject("Gold").transform;
            root.SetParent(parent, false);
            root.localPosition = EntityView.Tile(tile.Pos);
            shadowR = MeshKit.Obj("Glow", root, MeshKit.Disc, MeshKit.Trans, new Color(1, 0.8f, 0.3f, 0.22f),
                new Vector3(0, 0.012f, 0), new Vector3(0.5f, 1, 0.5f)).GetComponent<Renderer>();
            star = MeshKit.Obj("Star", root, MeshKit.Star, MeshKit.Trans, Palette.Gold,
                new Vector3(0, 0.38f, 0), Vector3.one * 0.42f).transform;
            starR = star.GetComponent<Renderer>();
        }

        public void Sync(Camera cam)
        {
            bool show = Tile.Explored && Tile.Gold > 0;
            if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
            if (!show) return;
            var c = Tile.Visible ? Palette.Gold : Color.Lerp(Palette.Gold, Palette.Explored, 0.55f);
            MeshKit.SetColor(starR, c);
            star.rotation = cam.transform.rotation;
            star.localPosition = new Vector3(0, 0.38f + Mathf.Sin(Time.time * 2f + Tile.Pos.x) * 0.04f, 0);
        }

        public void Destroy() => Object.Destroy(root.gameObject);
    }
}
