using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Strategy
{
    public enum GKey { Escape, Space, Enter, Tab, W, A, S, D, Up, Down, Left, Right, Alpha1, Alpha2, Alpha3, Alpha4, M, Z, Backspace, Q }

    /// <summary>Works with either the legacy Input Manager or the Input System package, whichever the project has enabled.</summary>
    public static class GInput
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        public static Vector2 MousePosition => Input.mousePosition;
        public static float Scroll => Input.mouseScrollDelta.y;
        public static bool MouseDown(int button) => Input.GetMouseButtonDown(button);
        public static bool MouseUp(int button) => Input.GetMouseButtonUp(button);
        public static bool MouseHeld(int button) => Input.GetMouseButton(button);
        public static bool KeyDown(GKey k) => Input.GetKeyDown(Map(k));
        public static bool KeyHeld(GKey k) => Input.GetKey(Map(k));

        static KeyCode Map(GKey k)
        {
            switch (k)
            {
                case GKey.Escape: return KeyCode.Escape;
                case GKey.Space: return KeyCode.Space;
                case GKey.Enter: return KeyCode.Return;
                case GKey.Tab: return KeyCode.Tab;
                case GKey.W: return KeyCode.W;
                case GKey.A: return KeyCode.A;
                case GKey.S: return KeyCode.S;
                case GKey.D: return KeyCode.D;
                case GKey.Up: return KeyCode.UpArrow;
                case GKey.Down: return KeyCode.DownArrow;
                case GKey.Left: return KeyCode.LeftArrow;
                case GKey.Right: return KeyCode.RightArrow;
                case GKey.Alpha1: return KeyCode.Alpha1;
                case GKey.Alpha2: return KeyCode.Alpha2;
                case GKey.Alpha3: return KeyCode.Alpha3;
                case GKey.Alpha4: return KeyCode.Alpha4;
                case GKey.M: return KeyCode.M;
                case GKey.Z: return KeyCode.Z;
                case GKey.Q: return KeyCode.Q;
                default: return KeyCode.Backspace;
            }
        }
#elif ENABLE_INPUT_SYSTEM
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool MouseDown(int button) => Button(button)?.wasPressedThisFrame ?? false;
        public static bool MouseUp(int button) => Button(button)?.wasReleasedThisFrame ?? false;
        public static bool MouseHeld(int button) => Button(button)?.isPressed ?? false;
        public static bool KeyDown(GKey k) => Keyboard.current != null && Keyboard.current[Map(k)].wasPressedThisFrame;
        public static bool KeyHeld(GKey k) => Keyboard.current != null && Keyboard.current[Map(k)].isPressed;

        static UnityEngine.InputSystem.Controls.ButtonControl Button(int b)
        {
            var m = Mouse.current;
            if (m == null) return null;
            return b == 0 ? m.leftButton : b == 1 ? m.rightButton : m.middleButton;
        }

        static Key Map(GKey k)
        {
            switch (k)
            {
                case GKey.Escape: return Key.Escape;
                case GKey.Space: return Key.Space;
                case GKey.Enter: return Key.Enter;
                case GKey.Tab: return Key.Tab;
                case GKey.W: return Key.W;
                case GKey.A: return Key.A;
                case GKey.S: return Key.S;
                case GKey.D: return Key.D;
                case GKey.Up: return Key.UpArrow;
                case GKey.Down: return Key.DownArrow;
                case GKey.Left: return Key.LeftArrow;
                case GKey.Right: return Key.RightArrow;
                case GKey.Alpha1: return Key.Digit1;
                case GKey.Alpha2: return Key.Digit2;
                case GKey.Alpha3: return Key.Digit3;
                case GKey.Alpha4: return Key.Digit4;
                case GKey.M: return Key.M;
                case GKey.Z: return Key.Z;
                case GKey.Q: return Key.Q;
                default: return Key.Backspace;
            }
        }
#endif
    }
}
