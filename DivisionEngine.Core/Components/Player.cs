//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine.Components
{
    /// <summary>
    /// Represents an entity with player controls (WASD, LShift, Mouse Look).
    /// </summary>
    public class Player : IComponent
    {
        public float movementSpeed = 4f;
        public float mouseSensitivity = 2f;
        public float sprintMultiplier = 2f;

        public IComponent Clone() => new Player
        {
            movementSpeed = movementSpeed,
            mouseSensitivity = mouseSensitivity,
            sprintMultiplier = sprintMultiplier,
        };
    }
}
