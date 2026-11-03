using UnityEngine;

namespace Strategy
{
    /// <summary>
    /// The sky behind the board: a screen-filling quad far behind everything, drawn by the Strategy/Backdrop shader.
    /// To use painted art instead, put an image at Assets/Resources/Art/Backdrop (any format Unity imports).
    /// </summary>
    public class Backdrop : MonoBehaviour
    {
        Camera cam;
        Transform quad;
        Material mat;

        public static void Attach(Camera cam)
        {
            if (cam.GetComponent<Backdrop>() != null) return;
            cam.gameObject.AddComponent<Backdrop>().Init(cam);
        }

        void Init(Camera c)
        {
            cam = c;
            var shader = Shader.Find("Strategy/Backdrop");
            if (shader == null) return;
            mat = new Material(shader);
            var art = Resources.Load<Texture2D>("Art/Backdrop");
            if (art != null)
            {
                mat.SetTexture("_MainTex", art);
                mat.SetFloat("_UseTex", 1f);
            }

            var mesh = new Mesh { name = "Backdrop" };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            var go = new GameObject("Backdrop");
            go.transform.SetParent(cam.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            quad = go.transform;
            LateUpdate();
        }

        void LateUpdate()
        {
            if (quad == null || cam == null) return;
            // Just inside the far plane, sized to fill the orthographic view at the current zoom.
            float h = cam.orthographic ? cam.orthographicSize * 2f : 2f * 150f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = cam.aspect > 0 ? cam.aspect : 16f / 9f;
            quad.localPosition = new Vector3(0, 0, cam.farClipPlane * 0.9f);
            quad.localRotation = Quaternion.identity;
            quad.localScale = new Vector3(h * aspect * 1.02f, h * 1.02f, 1f);
            mat.SetFloat("_Aspect", aspect);
        }
    }
}
