//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE.txt file in the project root
// for full license terms.
//
namespace DivisionEngine.Components
{
    /// <summary>
    /// Component that manages the invisible editor camera settings.
    /// </summary>
    public class EditorCamera : IComponent
    {
        public const uint EditorCameraId = 999999999; // High ID that won't conflict

        public float movementSpeed = 10f;
        public float mouseSensitivity = 2f;
        public float sprintMultiplier = 2f;

        public IComponent Clone() => new EditorCamera
        {
            movementSpeed = movementSpeed,
            mouseSensitivity = mouseSensitivity,
            sprintMultiplier = sprintMultiplier,
        };
    }
}
