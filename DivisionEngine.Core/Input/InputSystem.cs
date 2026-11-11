//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.MathUtilities;

namespace DivisionEngine.Input
{
    /// <summary>
    /// Represents a key code in the input system.
    /// </summary>
    public enum KeyCode
    {
        // Letters
        A = 0, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

        // Numbers (Top row)
        D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,

        // Function keys
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,

        // Control keys
        Escape,
        Tab,
        CapsLock,
        ShiftLeft,
        ShiftRight,
        ControlLeft,
        ControlRight,
        AltLeft,
        AltRight,
        Space,
        Enter,
        Backspace,

        // Navigation keys
        Insert,
        Delete,
        Home,
        End,
        PageUp,
        PageDown,

        // Arrow keys
        ArrowUp,
        ArrowDown,
        ArrowLeft,
        ArrowRight,

        // Symbols
        Minus,          // -
        Equals,         // =
        LeftBracket,    // [
        RightBracket,   // ]
        Backslash,      // \
        Semicolon,      // ;
        Quote,          // '
        Comma,          // ,
        Period,         // .
        Slash,          // /
        Grave,          // `

        // Numpad
        NumLock,
        Numpad0,
        Numpad1,
        Numpad2,
        Numpad3,
        Numpad4,
        Numpad5,
        Numpad6,
        Numpad7,
        Numpad8,
        Numpad9,
        NumpadDivide,
        NumpadMultiply,
        NumpadMinus,
        NumpadPlus,
        NumpadEnter,
        NumpadPeriod,

        // Misc
        PrintScreen,
        ScrollLock,
        PauseBreak,
        Menu,
        WindowsLeft,
        WindowsRight,
        Application,

        // Unknown
        Unknown
    }

    /// <summary>
    /// Represents a mouse key code in the input system.
    /// </summary>
    public enum MouseCode
    {
        // Main
        Left,
        Middle,
        Right,

        // Extra
        Button4,
        Button5,
        Button6,
        Button7,
        Button8,
        Button9,
        Button10,
        Button11,
        Button12,
        
        // Unknown
        Unknown
    };

    /// <summary>
    /// Handles input events and maintains input state in a thread-safe environment.
    /// </summary>
    public class InputSystem
    {
        /// <summary>
        /// Instance of the Input class, used for singleton access.
        /// </summary>
        public static InputSystem? Instance { get; private set; }

        private readonly HashSet<KeyCode> pressedKeys;
        private readonly HashSet<MouseCode> pressedMouseKeys;
        private readonly Lock syncLock;

        private float2 mousePos, mouseUV, mouseDelta, mouseUVDelta, scrollDelta;

        /// <summary>
        /// Retrieve the mouse position on screen.
        /// </summary>
        public static float2 MousePosition { get { lock(Instance!.syncLock) return Instance.mousePos; } }

        /// <summary>
        /// Retrieve the mouse position on screen.
        /// </summary>
        public static float2 MouseUV { get { lock (Instance!.syncLock) return Instance.mouseUV; } }

        /// <summary>
        /// Retrieve the mouse delta between last mouse position record.
        /// </summary>
        public static float2 MouseDelta { get { lock (Instance!.syncLock) return Instance.mouseDelta; } }

        /// <summary>
        /// Retrieve the mouse delta between last mouse position record.
        /// </summary>
        public static float2 MouseUVDelta { get { lock (Instance!.syncLock) return Instance.mouseUVDelta; } }

        /// <summary>
        /// Retrieve the mouse scroll wheel delta.
        /// </summary>
        public static float2 ScrollDelta { get { lock (Instance!.syncLock) return Instance.scrollDelta; } }

        /// <summary>
        /// Initializes a new input system singleton.
        /// </summary>
        /// <remarks>(this should only be called once)</remarks>
        public InputSystem()
        {
            syncLock = new Lock();
            pressedKeys = [];
            pressedMouseKeys = [];

            mousePos = float2.zero;
            mouseUV = float2.zero;
            mouseDelta = float2.zero;
            mouseUVDelta = float2.zero;
            Instance = this;
        }

        /// <summary>
        /// Updates on the fixed update loop from the current world (or on editor update loop).
        /// </summary>
        public void InvokeInputUpdate()
        {
            if (mouseDelta.x != 0 || mouseDelta.y != 0) mouseDelta = float2.zero;
            if (mouseUVDelta.x != 0 || mouseUVDelta.y != 0) mouseUVDelta = float2.zero;
            if (scrollDelta.x != 0 || scrollDelta.y != 0) scrollDelta = float2.zero;
        }

        public void SetKeyDown(KeyCode key)
        {
            lock (syncLock) pressedKeys.Add(key);
        }

        public void SetKeyUp(KeyCode key)
        {
            lock (syncLock) pressedKeys.Remove(key);
        }

        public void SetMouseKeyDown(MouseCode mouseKey)
        {
            lock (syncLock) pressedMouseKeys.Add(mouseKey);
        }

        public void SetMouseKeyUp(MouseCode mouseKey)
        {
            lock (syncLock) pressedMouseKeys.Remove(mouseKey);
        }

        public void SetMousePosition(float2 newMousePos)
        {
            lock (syncLock)
            {
                mouseDelta = newMousePos - mousePos;
                mousePos = newMousePos;
            }
        }

        public void SetMouseWheel(float2 newWheelDelta)
        {
            lock (syncLock) scrollDelta = newWheelDelta;
        }

        public void SetRelativeMousePosition(float2 newMousePos, float2 screenSize)
        {
            lock (syncLock)
            {
                if (screenSize.x > 0f && screenSize.y > 0f)
                {
                    float2 relMousePos = newMousePos.Divide(screenSize);
                    mouseUVDelta = relMousePos.Subtract(mouseUV);
                    mouseUV = relMousePos;
                }
            }
        }

        /// <summary>
        /// Checks if a key is currently pressed on the default keyboard.
        /// </summary>
        /// <param name="key">The key to check</param>
        /// <returns>Whether the key is pressed or not</returns>
        public static bool IsPressed(KeyCode key)
        {
            lock (Instance!.syncLock) return Instance.pressedKeys.Contains(key);
        }

        /// <summary>
        /// Checks if any control key is pressed.
        /// </summary>
        public static bool IsCtrlPressed()
        {
            lock (Instance!.syncLock) return Instance.pressedKeys.Contains(KeyCode.ControlLeft)
                    || Instance.pressedKeys.Contains(KeyCode.ControlRight);
        }

        /// <summary>
        /// Checks if any alt key is pressed.
        /// </summary>
        public static bool IsAltPressed()
        {
            lock (Instance!.syncLock) return Instance.pressedKeys.Contains(KeyCode.AltLeft)
                    || Instance.pressedKeys.Contains(KeyCode.AltRight);
        }

        /// <summary>
        /// Checks if any shift key is pressed.
        /// </summary>
        public static bool IsShiftPressed()
        {
            lock (Instance!.syncLock) return Instance.pressedKeys.Contains(KeyCode.ShiftLeft)
                    || Instance.pressedKeys.Contains(KeyCode.ShiftRight);
        }

        /// <summary>
        /// Checks if a mouse button is currently pressed on the default mouse.
        /// </summary>
        /// <param name="key">Mouse button to check</param>
        /// <returns>Whether the mouse button is pressed or not</returns>
        public static bool IsMousePressed(MouseCode key)
        {
            lock (Instance!.syncLock) return Instance.pressedMouseKeys.Contains(key);
        }

        /// <summary>
        /// Accumulates the mouse UV delta over time. (Used in editor)
        /// </summary>
        /// <param name="uvDelta">UV delta to accumulate</param>
        public void AccumulateMouseUVDelta(float2 uvDelta)
        {
            lock (syncLock)
            {
                mouseUVDelta = mouseUVDelta.Add(uvDelta);
                mouseUV = mouseUV.Add(uvDelta); // keep mouseUV consistent for anything else reading it
            }
        }
    }
}
