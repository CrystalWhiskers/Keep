using UnityEngine;
using UnityEngine.EventSystems;

namespace Strategy
{
    /// <summary>Orthographic isometric camera. WASD/arrows or right/middle-drag to pan, scroll to zoom.</summary>
    public class CameraRig : MonoBehaviour
    {
        public const float Pitch = 30f, Yaw = 45f;
        public Camera Cam { get; private set; }
        public Vector3 Focus;
        public bool InputEnabled = true;
        /// <summary>Set for one frame when the right button was released without dragging (used as "cancel").</summary>
        public bool RightClicked { get; private set; }

        Vector3 targetFocus;
        float size = 4.6f, targetSize = 4.6f;
        Vector2 min, max;
        Vector2 dragStart;
        Vector3 dragFocus;
        int dragButton = -1;
        bool dragging;

        public void Init(Vector2 minBounds, Vector2 maxBounds, Vector3 start)
        {
            Cam = GetComponent<Camera>();
            if (Cam == null) Cam = gameObject.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Palette.Background;
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 200f;
            transform.rotation = Quaternion.Euler(Pitch, Yaw, 0);
            min = minBounds;
            max = maxBounds;
            Focus = targetFocus = Clamp(start);
            Apply();
        }

        public void JumpTo(Vector3 p, bool instant = false)
        {
            targetFocus = Clamp(p);
            if (instant) Focus = targetFocus;
        }

        Vector3 Clamp(Vector3 p) => new Vector3(Mathf.Clamp(p.x, min.x, max.x), 0, Mathf.Clamp(p.z, min.y, max.y));

        void LateUpdate()
        {
            if (Cam == null) return;
            RightClicked = false;
            if (InputEnabled) HandleInput();
            Focus = Vector3.Lerp(Focus, targetFocus, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            size = Mathf.Lerp(size, targetSize, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            Apply();
        }

        void HandleInput()
        {
            var flatFwd = new Vector3(transform.forward.x, 0, transform.forward.z).normalized;
            var flatRight = new Vector3(transform.right.x, 0, transform.right.z).normalized;

            var move = Vector2.zero;
            if (GInput.KeyHeld(GKey.W) || GInput.KeyHeld(GKey.Up)) move.y += 1;
            if (GInput.KeyHeld(GKey.S) || GInput.KeyHeld(GKey.Down)) move.y -= 1;
            if (GInput.KeyHeld(GKey.D) || GInput.KeyHeld(GKey.Right)) move.x += 1;
            if (GInput.KeyHeld(GKey.A) || GInput.KeyHeld(GKey.Left)) move.x -= 1;
            if (move != Vector2.zero)
                targetFocus = Clamp(targetFocus + (flatRight * move.x + flatFwd * move.y * 2f) * (size * 1.2f * Time.unscaledDeltaTime));

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            float scroll = GInput.Scroll;
            if (!overUi && Mathf.Abs(scroll) > 0.01f) targetSize = Mathf.Clamp(targetSize - scroll * 0.6f, 3.2f, 9f);

            for (int b = 1; b <= 2; b++)
            {
                if (GInput.MouseDown(b) && !overUi && dragButton < 0)
                {
                    dragButton = b;
                    dragging = false;
                    dragStart = GInput.MousePosition;
                    dragFocus = targetFocus;
                }
            }
            if (dragButton >= 0)
            {
                var mp = GInput.MousePosition;
                if (!dragging && (mp - dragStart).magnitude > 6f) dragging = true;
                if (dragging)
                {
                    // Convert the screen-space drag into a ground-plane offset at the current zoom.
                    var delta = mp - dragStart;
                    float unitsPerPixel = Cam.orthographicSize * 2f / Screen.height;
                    var worldDelta = flatRight * delta.x * unitsPerPixel + flatFwd * delta.y * unitsPerPixel * 2f;
                    targetFocus = Clamp(dragFocus - worldDelta);
                    Focus = targetFocus;
                }
                if (GInput.MouseUp(dragButton))
                {
                    if (!dragging && dragButton == 1) RightClicked = true;
                    dragButton = -1;
                }
            }
        }

        void Apply()
        {
            Cam.orthographicSize = size;
            transform.position = Focus - transform.forward * 40f;
        }

        public bool GroundPoint(Vector2 screen, out Vector3 point)
        {
            var ray = Cam.ScreenPointToRay(screen);
            point = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            point = ray.origin + ray.direction * t;
            return true;
        }

        /// <summary>The four corners of the view on the ground, for the minimap's camera box.</summary>
        public Vector3[] GroundCorners()
        {
            var r = new Vector3[4];
            var vp = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            for (int i = 0; i < 4; i++)
            {
                var ray = Cam.ViewportPointToRay(vp[i]);
                float t = -ray.origin.y / ray.direction.y;
                r[i] = ray.origin + ray.direction * t;
            }
            return r;
        }
    }
}
