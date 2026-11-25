//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
namespace DivisionEngine.Tests
{
    /// <summary>
    /// Used for testing the input system
    /// </summary>
    internal class TestInputSystem : SystemBase
    {
        public override void Update()
        {
            //Info($"K key is pressed: {InputSystem.IsPressed(KeyCode.K)}");
            //Info($"Left mouse is pressed: {InputSystem.IsMousePressed(MouseCode.Left)}");
            //Info($"Mouse position: ({InputSystem.MouseUV.X}, {InputSystem.MouseUV.Y})");
        }
    }
}
