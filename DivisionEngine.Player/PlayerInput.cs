//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using DivisionEngine.Input;
using Silk.NET.Input;

namespace DivisionEngine.Player
{
    /// <summary>
    /// Handles input extensions for the Division Engine player.
    /// </summary>
    internal static class PlayerInput
    {
        /// <summary>
        /// Converts a <see cref="Key"/> value from Silk.NET to its corresponding <see cref="KeyCode"/> value.
        /// </summary>
        /// <param name="silkKey">The Silk.NET <see cref="Key"/> to be converted.</param>
        /// <returns>The corresponding <see cref="KeyCode"/> value that matches the provided Silk.NET <see cref="Key"/>.</returns>
        public static KeyCode SilkNetToKeyCode(Key silkKey)
        {
            return silkKey switch
            {
                // Letters
                Key.A => KeyCode.A,
                Key.B => KeyCode.B,
                Key.C => KeyCode.C,
                Key.D => KeyCode.D,
                Key.E => KeyCode.E,
                Key.F => KeyCode.F,
                Key.G => KeyCode.G,
                Key.H => KeyCode.H,
                Key.I => KeyCode.I,
                Key.J => KeyCode.J,
                Key.K => KeyCode.K,
                Key.L => KeyCode.L,
                Key.M => KeyCode.M,
                Key.N => KeyCode.N,
                Key.O => KeyCode.O,
                Key.P => KeyCode.P,
                Key.Q => KeyCode.Q,
                Key.R => KeyCode.R,
                Key.S => KeyCode.S,
                Key.T => KeyCode.T,
                Key.U => KeyCode.U,
                Key.V => KeyCode.V,
                Key.W => KeyCode.W,
                Key.X => KeyCode.X,
                Key.Y => KeyCode.Y,
                Key.Z => KeyCode.Z,

                // Numbers
                Key.Number0 => KeyCode.D0,
                Key.Number1 => KeyCode.D1,
                Key.Number2 => KeyCode.D2,
                Key.Number3 => KeyCode.D3,
                Key.Number4 => KeyCode.D4,
                Key.Number5 => KeyCode.D5,
                Key.Number6 => KeyCode.D6,
                Key.Number7 => KeyCode.D7,
                Key.Number8 => KeyCode.D8,
                Key.Number9 => KeyCode.D9,

                // Function keys
                Key.F1 => KeyCode.F1,
                Key.F2 => KeyCode.F2,
                Key.F3 => KeyCode.F3,
                Key.F4 => KeyCode.F4,
                Key.F5 => KeyCode.F5,
                Key.F6 => KeyCode.F6,
                Key.F7 => KeyCode.F7,
                Key.F8 => KeyCode.F8,
                Key.F9 => KeyCode.F9,
                Key.F10 => KeyCode.F10,
                Key.F11 => KeyCode.F11,
                Key.F12 => KeyCode.F12,

                // Control keys
                Key.Escape => KeyCode.Escape,
                Key.Tab => KeyCode.Tab,
                Key.CapsLock => KeyCode.CapsLock,
                Key.ShiftLeft => KeyCode.ShiftLeft,
                Key.ShiftRight => KeyCode.ShiftRight,
                Key.ControlLeft => KeyCode.ControlLeft,
                Key.ControlRight => KeyCode.ControlRight,
                Key.AltLeft => KeyCode.AltLeft,
                Key.AltRight => KeyCode.AltRight,
                Key.Space => KeyCode.Space,
                Key.Enter => KeyCode.Enter,
                Key.Backspace => KeyCode.Backspace,

                // Navigation keys
                Key.Insert => KeyCode.Insert,
                Key.Delete => KeyCode.Delete,
                Key.Home => KeyCode.Home,
                Key.End => KeyCode.End,
                Key.PageUp => KeyCode.PageUp,
                Key.PageDown => KeyCode.PageDown,

                // Arrow keys
                Key.Up => KeyCode.ArrowUp,
                Key.Down => KeyCode.ArrowDown,
                Key.Left => KeyCode.ArrowLeft,
                Key.Right => KeyCode.ArrowRight,

                // Symbols
                Key.Minus => KeyCode.Minus,
                Key.Equal => KeyCode.Equals,
                Key.LeftBracket => KeyCode.LeftBracket,
                Key.RightBracket => KeyCode.RightBracket,
                Key.BackSlash => KeyCode.Backslash,
                Key.Semicolon => KeyCode.Semicolon,
                Key.Apostrophe => KeyCode.Quote,
                Key.Comma => KeyCode.Comma,
                Key.Period => KeyCode.Period,
                Key.Slash => KeyCode.Slash,
                Key.GraveAccent => KeyCode.Grave,

                // Numpad
                Key.NumLock => KeyCode.NumLock,
                Key.Keypad0 => KeyCode.Numpad0,
                Key.Keypad1 => KeyCode.Numpad1,
                Key.Keypad2 => KeyCode.Numpad2,
                Key.Keypad3 => KeyCode.Numpad3,
                Key.Keypad4 => KeyCode.Numpad4,
                Key.Keypad5 => KeyCode.Numpad5,
                Key.Keypad6 => KeyCode.Numpad6,
                Key.Keypad7 => KeyCode.Numpad7,
                Key.Keypad8 => KeyCode.Numpad8,
                Key.Keypad9 => KeyCode.Numpad9,
                Key.KeypadDivide => KeyCode.NumpadDivide,
                Key.KeypadMultiply => KeyCode.NumpadMultiply,
                Key.KeypadSubtract => KeyCode.NumpadMinus,
                Key.KeypadAdd => KeyCode.NumpadPlus,
                Key.KeypadEnter => KeyCode.NumpadEnter,
                Key.KeypadDecimal => KeyCode.NumpadPeriod,

                // Other keys
                Key.PrintScreen => KeyCode.PrintScreen,
                Key.ScrollLock => KeyCode.ScrollLock,
                Key.SuperLeft => KeyCode.WindowsLeft,
                Key.SuperRight => KeyCode.WindowsRight,

                // Handle no mapping
                _ => KeyCode.Unknown
            };
        }

        /// <summary>
        /// Convert a Silk.Net mouse button to a Division Engine mouse code.
        /// </summary>
        /// <param name="mouseButton">Silk.Net mouse button</param>
        /// <returns>Division engine mouse code mapping</returns>
        public static MouseCode SilkNetToMouseCode(MouseButton mouseButton)
        {
            return mouseButton switch
            {
                // Main
                MouseButton.Left => MouseCode.Left,
                MouseButton.Right => MouseCode.Right,
                MouseButton.Middle => MouseCode.Middle,

                // Extra
                MouseButton.Button4 => MouseCode.Button4,
                MouseButton.Button5 => MouseCode.Button5,
                MouseButton.Button6 => MouseCode.Button6,
                MouseButton.Button7 => MouseCode.Button7,
                MouseButton.Button8 => MouseCode.Button8,
                MouseButton.Button9 => MouseCode.Button9,
                MouseButton.Button10 => MouseCode.Button10,
                MouseButton.Button11 => MouseCode.Button11,
                MouseButton.Button12 => MouseCode.Button12,

                // Handle no mapping
                _ => MouseCode.Unknown
            };
        }
    }
}
