//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Input;
using DivisionEngine.MathUtilities;
using DivisionEngine.Settings;
using DivisionEngine.Systems;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// A System that manages the editor camera.
    /// </summary>
    public class EditorCameraSystem : SystemBase
    {
        public override void EditorUpdate()
        {
            Camera? editorCamera;
            if (EngineCore.IsInPlayMode && !EngineCore.IsPaused)
            {
                // Re-activate all non-editor cameras
                foreach (var (entity, camera) in W.QueryData<Camera>())
                    if (!camera.isActive && !W.HasComponent<EditorCamera>(entity))
                        camera.isActive = true;

                // Deactivate the editor camera
                editorCamera = W.GetComponent<Camera>(EditorCamera.EditorCameraId);
                if (editorCamera != null && !editorCamera.isActive) editorCamera.isActive = false;
            }
            else
            {
                EnsureEditorCamera();

                // Deactivate all non-editor cameras
                foreach (var (entity, camera) in W.QueryData<Camera>())
                    if (camera.isActive && !W.HasComponent<EditorCamera>(entity))
                        camera.isActive = false;

                // Activate the editor camera
                editorCamera = W.GetComponent<Camera>(EditorCamera.EditorCameraId);
                if (editorCamera != null && !editorCamera.isActive) editorCamera.isActive = true;

                HandleEditorCameraInput();
            }
        }

        private static void EnsureEditorCamera()
        {
            // Check if editor camera already exists and verify its components if it does
            if (EditorCamera.EditorCameraId != 0 && W.EntityExists(EditorCamera.EditorCameraId) &&
                W.HasComponent<Transform>(EditorCamera.EditorCameraId) &&
                W.HasComponent<Camera>(EditorCamera.EditorCameraId) &&
                W.HasComponent<EditorCamera>(EditorCamera.EditorCameraId))
                return;

            // Rebuild editor camera entity
            if (EditorCamera.EditorCameraId != 0 && W.EntityExists(EditorCamera.EditorCameraId))
                W.DestroyEntity(EditorCamera.EditorCameraId);
            WorldManager.CurrentWorld?.entities.Add(EditorCamera.EditorCameraId);

            // Find existing camera to copy position from
            float3 defaultPosition = new float3(0, 2, 7);
            float4 defaultRotation = Quaternion.Identity;

            foreach (var (entity, transform, _) in W.QueryData<Transform, Camera>())
            {
                if (entity != EditorCamera.EditorCameraId)
                {
                    defaultPosition = transform.position;
                    defaultRotation = transform.rotation;
                    break;
                }
            }

            // Add components
            W.AddComponent(EditorCamera.EditorCameraId, new Transform
            {
                position = defaultPosition,
                rotation = defaultRotation
            });
            W.AddComponent(EditorCamera.EditorCameraId, new Camera());
            W.AddComponent(EditorCamera.EditorCameraId, new EditorCamera());

            Debug.Info($"Editor camera created with ID: {EditorCamera.EditorCameraId}");
        }

        private static void HandleEditorCameraInput()
        {
            Transform? transform = W.GetComponent<Transform>(EditorCamera.EditorCameraId);
            EditorCamera? editorCamera = W.GetComponent<EditorCamera>(EditorCamera.EditorCameraId);

            if (transform == null || editorCamera == null) return;

            HandleKeyboardMovement(transform, editorCamera);
            HandleMouseLook(transform, editorCamera);
        }

        private static void HandleKeyboardMovement(Transform transform, EditorCamera editorCamera)
        {
            float deltaTime = TimeSystem.DeltaTimeF;
            float speed = editorCamera.movementSpeed * deltaTime;

            if (InputSystem.IsPressed(KeyCode.ShiftLeft) || InputSystem.IsPressed(KeyCode.ShiftRight))
                speed *= editorCamera.sprintMultiplier;

            float3 position = transform.position;
            float3 forward = transform.Forward;
            float3 right = transform.Right;
            float3 up = transform.Up;

            float3 movement = new float3(0f, 0f, 0f);
            if (InputSystem.IsPressed(KeyCode.W) || InputSystem.IsPressed(KeyCode.ArrowUp))
                movement = movement.Add(forward.Multiply(speed));
            if (InputSystem.IsPressed(KeyCode.A) || InputSystem.IsPressed(KeyCode.ArrowLeft))
                movement = movement.Subtract(right.Multiply(speed));
            if (InputSystem.IsPressed(KeyCode.S) || InputSystem.IsPressed(KeyCode.ArrowDown))
                movement = movement.Subtract(forward.Multiply(speed));
            if (InputSystem.IsPressed(KeyCode.D) || InputSystem.IsPressed(KeyCode.ArrowRight))
                movement = movement.Add(right.Multiply(speed));
            if (InputSystem.IsPressed(KeyCode.Q) || InputSystem.IsPressed(KeyCode.PageDown))
                movement = movement.Subtract(up.Multiply(speed));
            if (InputSystem.IsPressed(KeyCode.E) || InputSystem.IsPressed(KeyCode.PageUp))
                movement = movement.Add(up.Multiply(speed));

            editorCamera.movementSpeed += InputSystem.ScrollDelta.Y * editorCamera.movementSpeed * 0.05f;
            position = position.Add(movement);
            transform.position = position;
        }

        private static void HandleMouseLook(Transform transform, EditorCamera editorCamera)
        {
            if (InputSystem.IsMousePressed(MouseCode.Right))
            {
                float2 mouseDelta = InputSystem.MouseUVDelta;
                if (mouseDelta.X == 0f && mouseDelta.Y == 0f) return;

                EngineSettings settings = EngineSettings.Instance;
                // Positive rotation about +Y turns the view to the left, so yaw is
                // negated to keep "drag mouse right -> look right".
                float yaw = -mouseDelta.X * editorCamera.mouseSensitivity * settings.MouseSensitivity;
                float pitch = -mouseDelta.Y * editorCamera.mouseSensitivity * settings.MouseSensitivity;

                float4 currentRot = transform.rotation;
                float4 yawRot = Quaternion.CreateFromAxisAngle(new float3(0, 1, 0), yaw);
                float4 pitchRot = Quaternion.CreateFromAxisAngle(new float3(1, 0, 0), pitch);

                // Pitch around the camera's LOCAL right axis, then yaw around WORLD up:
                //     q' = Ry * q * Rx
                // Keeping that order means q always stays a pure yaw-then-pitch rotation,
                // so the camera's right vector never leaves the horizon (no barrel roll).
                // The old order (pitchRot * q * yawRot) pitched around world X instead,
                // which turns into roll as soon as the camera has any yaw.
                float4 newRot = Quaternion.Multiply(currentRot, pitchRot);
                newRot = Quaternion.Multiply(yawRot, newRot);
                transform.rotation = newRot.Normalize();
            }
        }
    }
}
