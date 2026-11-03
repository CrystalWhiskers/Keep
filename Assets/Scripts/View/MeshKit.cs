using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Strategy
{
    /// <summary>Procedural meshes and the two shared materials. Every shape in the game is built here.</summary>
    public static class MeshKit
    {
        static Material flat, trans;
        static Mesh box, sphere, disc, star, quad;
        static readonly Dictionary<string, Mesh> cylinders = new Dictionary<string, Mesh>();
        static MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        public static Material Flat => flat != null ? flat : flat = new Material(FindShader("Strategy/Flat"));
        public static Material Trans => trans != null ? trans : trans = new Material(FindShader("Strategy/FlatTransparent"));

        static Shader FindShader(string name)
        {
            var s = Shader.Find(name);
            if (s == null)
            {
                Debug.LogError($"Shader {name} not found; falling back to Unlit/Color");
                s = Shader.Find("Unlit/Color");
            }
            return s;
        }

        /// <summary>Vertex colours are not colour-space converted by Unity, so convert them ourselves.</summary>
        public static Color Lin(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        public static GameObject Obj(string name, Transform parent, Mesh mesh, Material mat, Color color,
            Vector3 localPos, Vector3 scale, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (rot.HasValue) go.transform.localRotation = rot.Value;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            SetColor(r, color);
            return go;
        }

        public static void SetColor(Renderer r, Color c)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(mpb);
        }

        // ------------------------------------------------------------ primitive meshes (white vertex colours)

        public static Mesh Box
        {
            get
            {
                if (box != null) return box;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
                void Face(Vector3 normal, Vector3 u, Vector3 w)
                {
                    int i = v.Count;
                    var c = normal * 0.5f;
                    v.Add(c - u * 0.5f - w * 0.5f); v.Add(c - u * 0.5f + w * 0.5f);
                    v.Add(c + u * 0.5f + w * 0.5f); v.Add(c + u * 0.5f - w * 0.5f);
                    for (int k = 0; k < 4; k++) n.Add(normal);
                    t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
                Face(Vector3.up, Vector3.right, Vector3.forward);
                Face(Vector3.down, Vector3.forward, Vector3.right);
                Face(Vector3.forward, Vector3.up, Vector3.right);
                Face(Vector3.back, Vector3.right, Vector3.up);
                Face(Vector3.right, Vector3.forward, Vector3.up);
                Face(Vector3.left, Vector3.up, Vector3.forward);
                return box = Build("Box", v, n, t);
            }
        }

        /// <summary>Cylinder or cone from y=0 to y=1, radii relative to a unit footprint.</summary>
        public static Mesh Cylinder(float rBottom = 0.5f, float rTop = 0.5f, int seg = 14)
        {
            string key = $"{rBottom}_{rTop}_{seg}";
            if (cylinders.TryGetValue(key, out var m)) return m;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            float slope = (rBottom - rTop);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                int b = v.Count;
                v.Add(d0 * rBottom); v.Add(d0 * rTop + Vector3.up); v.Add(d1 * rTop + Vector3.up); v.Add(d1 * rBottom);
                var n0 = (d0 + Vector3.up * slope).normalized;
                var n1 = (d1 + Vector3.up * slope).normalized;
                n.Add(n0); n.Add(n0); n.Add(n1); n.Add(n1);
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                if (rTop > 0)
                {
                    int c = v.Count;
                    v.Add(Vector3.up); v.Add(d1 * rTop + Vector3.up); v.Add(d0 * rTop + Vector3.up);
                    n.Add(Vector3.up); n.Add(Vector3.up); n.Add(Vector3.up);
                    t.AddRange(new[] { c, c + 1, c + 2 });
                }
            }
            m = Build("Cylinder", v, n, t);
            cylinders[key] = m;
            return m;
        }

        public static Mesh Sphere
        {
            get
            {
                if (sphere != null) return sphere;
                var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
                const int lat = 8, lon = 14;
                for (int i = 0; i <= lat; i++)
                {
                    float th = i * Mathf.PI / lat;
                    for (int j = 0; j <= lon; j++)
                    {
                        float ph = j * Mathf.PI * 2 / lon;
                        var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        v.Add(d * 0.5f); n.Add(d);
                    }
                }
                for (int i = 0; i < lat; i++)
                    for (int j = 0; j < lon; j++)
                    {
                        int a = i * (lon + 1) + j, b = a + lon + 1;
                        t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                    }
                return sphere = Build("Sphere", v, n, t);
            }
        }

        /// <summary>Flat disc on the XZ plane, radius 0.5.</summary>
        public static Mesh Disc
        {
            get
            {
                if (disc != null) return disc;
                var v = new List<Vector3> { Vector3.zero }; var n = new List<Vector3> { Vector3.up }; var t = new List<int>();
                const int seg = 28;
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2 / seg;
                    v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.5f);
                    n.Add(Vector3.up);
                    if (i > 0) t.AddRange(new[] { 0, i + 1, i });
                }
                return disc = Build("Disc", v, n, t);
            }
        }

        /// <summary>Four-point sparkle on the XY plane, like the gold pickups in the mockup.</summary>
        public static Mesh Star
        {
            get
            {
                if (star != null) return star;
                var v = new List<Vector3> { Vector3.zero }; var n = new List<Vector3> { Vector3.back }; var t = new List<int>();
                const int pts = 8;
                for (int i = 0; i < pts; i++)
                {
                    float a = i * Mathf.PI * 2 / pts + Mathf.PI / 2;
                    float r = i % 2 == 0 ? 0.5f : 0.14f;
                    v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
                    n.Add(Vector3.back);
                }
                for (int i = 0; i < pts; i++) t.AddRange(new[] { 0, 1 + (i + 1) % pts, 1 + i });
                return star = Build("Star", v, n, t);
            }
        }

        /// <summary>Unit quad on the XZ plane, centred.</summary>
        public static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                var v = new List<Vector3> { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f), new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
                var n = new List<Vector3> { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                return quad = Build("Quad", v, n, new List<int> { 0, 1, 2, 0, 2, 3 });
            }
        }

        static Mesh Build(string name, List<Vector3> v, List<Vector3> n, List<int> t)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            var white = new Color[v.Count];
            for (int i = 0; i < white.Length; i++) white[i] = Color.white;
            m.colors = white;
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------ line meshes on the ground

        /// <summary>Adds a flat line segment (as a quad) lying on the XZ plane at height y.</summary>
        public static void AddLine(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, float width)
        {
            var dir = (b - a).normalized;
            var side = new Vector3(-dir.z, 0, dir.x) * (width * 0.5f);
            a -= dir * (width * 0.5f);
            b += dir * (width * 0.5f);
            int i = v.Count;
            v.Add(a - side); v.Add(a + side); v.Add(b + side); v.Add(b - side);
            t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        public static void Fill(Mesh m, List<Vector3> v, List<int> t)
        {
            m.Clear();
            m.SetVertices(v);
            var c = new Color[v.Count];
            for (int i = 0; i < c.Length; i++) c[i] = Color.white;
            m.colors = c;
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
        }
    }
}
