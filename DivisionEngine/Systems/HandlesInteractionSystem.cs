//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using DivisionEngine.Components;
using DivisionEngine.Editor.Settings;
using DivisionEngine.Input;
using DivisionEngine.MathLib;
using DivisionEngine.MathUtilities;
using DivisionEngine.Rendering;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Handles editor transform gizmo interaction.
    /// </summary>
    public class HandleInteractionSystem : SystemBase
    {
        public override int Priority => -10;

        public static bool IsDragging { get; private set; } = false;

        private static uint selectedHandle = 0;
        private static uint draggedEntity = uint.MaxValue;

        // Translation state
        private static float3 currentPosition;
        private static float2 lastMousePos;
        private static float3 cameraPosition;
        private static float3 cameraRight;
        private static float3 cameraUp;
        private static float distanceToCamera;

        // Rotation state
        private static float3 rotationAxis;
        private static float lastAngle;

        // Scaling state
        private static float3 currentScale;

        public override void AppStart()
        {
            Selection.OnSelectionChanged += (t) =>
            {
                if (Selection.SelectedType == SelectionType.Entity && t is uint entity)
                {
                    draggedEntity = entity;
                    Debug.Info($"HandleInteraction: Entity selected {t}");
                }
                else
                {
                    draggedEntity = uint.MaxValue;
                    RenderPipeline.Instance?.HideHandles();
                    Debug.Info("HandleInteraction: No entity selected");
                }
            };
            EngineCore.PlayModeChanged += EngineCore_PlayModeChanged;
        }

        private void EngineCore_PlayModeChanged(bool inPlayMode)
        {
            if (inPlayMode) RenderPipeline.Instance?.HideHandles();
        }

        public override void EditorUpdate()
        {
            if (RenderPipeline.Instance == null || EngineCore.IsInPlayMode) return;

            // Always update handle position to match selected entity's transform
            if (draggedEntity != uint.MaxValue && !IsDragging)
            {
                Transform? transform = W.GetComponent<Transform>(draggedEntity);
                if (transform != null) RenderPipeline.Instance?.ShowHandles(transform.position, EditorSettings.Instance!.EditorHandleScale);
                else RenderPipeline.Instance?.HideHandles();
            }

            float2 mousePos = InputSystem.MousePosition;
            int pixelX = (int)mousePos.X;
            int pixelY = (int)mousePos.Y;

            // Validate mouse position is within render area before using it
            int width, height;
            if (RenderPipeline.Instance?.Mode == RenderPipeline.RunMode.Embedded)
            {
                width = RenderPipeline.Instance.EmbeddedWidth;
                height = RenderPipeline.Instance.EmbeddedHeight;
            }
            else if (RenderPipeline.Instance?.RendererWindow != null)
            {
                width = RenderPipeline.Instance.RendererWindow.Size.X;
                height = RenderPipeline.Instance.RendererWindow.Size.Y;
            }
            else return;

            // Only update hover if mouse is within bounds
            if (!IsDragging && pixelX >= 0 && pixelX < width && pixelY >= 0 && pixelY < height)
                RenderPipeline.Instance!.UpdateHoveredHandle(pixelX, pixelY);

            // Start dragging
            bool mouseDown = InputSystem.IsMousePressed(MouseCode.Left);
            if (mouseDown && !IsDragging)
            {
                uint handleId = 0;
                if (pixelX >= 0 && pixelX < width && pixelY >= 0 && pixelY < height)
                    handleId = RenderPipeline.Instance.GetHandleAtPosition(pixelX, pixelY);

                // Get camera data first (needed for all operations)
                Transform? camTransform = GetMainCamera();
                if (camTransform == null)
                {
                    Debug.Error("HandleInteraction: No camera found");
                    return;
                }
                cameraPosition = camTransform.position;
                cameraRight = camTransform.Right;
                cameraUp = camTransform.Up;

                Transform? entityTransform = W.GetComponent<Transform>(draggedEntity);
                if (entityTransform == null && handleId > 0)
                {
                    Debug.Error($"HandleInteraction: No Transform component on entity {draggedEntity}");
                    return;
                }

                // Check for icon clicks first
                //uint clickedIconId = RenderPipeline.Instance.GetIconAtPosition(pixelX, pixelY);
                //if (clickedIconId != 0)
                //{
                //    Selection.SelectEntity(clickedIconId);
                //    return;
                //}

                // Check for custom shape clicks
                //uint clickedShapeId = RenderPipeline.Instance.GetCustomShapeAtPosition(pixelX, pixelY);
                //if (clickedShapeId != 0 && customShapes.TryGetValue(clickedShapeId, out var shape) && shape.EntityId.HasValue)
                //{
                //    Selection.SelectEntity(shape.EntityId.Value);
                //    return;
                //}

                // Translation handles (1-3)
                if (handleId >= 1 && handleId <= 3 && draggedEntity != uint.MaxValue)
                {
                    IsDragging = true;
                    selectedHandle = handleId;
                    lastMousePos = mousePos;

                    currentPosition = entityTransform!.position;
                    distanceToCamera = math.sqrt(
                        (cameraPosition.X - currentPosition.X) * (cameraPosition.X - currentPosition.X) +
                        (cameraPosition.Y - currentPosition.Y) * (cameraPosition.Y - currentPosition.Y) +
                        (cameraPosition.Z - currentPosition.Z) * (cameraPosition.Z - currentPosition.Z)
                    );
                    Debug.Info($"HandleInteraction: Started translation on handle {selectedHandle}, position {currentPosition}");
                }
                // Scale handles (5-7)
                else if (handleId >= 5 && handleId <= 7 && draggedEntity != uint.MaxValue)
                {
                    IsDragging = true;
                    selectedHandle = handleId;
                    lastMousePos = mousePos;

                    currentScale = entityTransform!.scaling;

                    // Use the entity's position for distance calculation
                    float3 entityPos = entityTransform.position;
                    distanceToCamera = math.sqrt(
                        (cameraPosition.X - entityPos.X) * (cameraPosition.X - entityPos.X) +
                        (cameraPosition.Y - entityPos.Y) * (cameraPosition.Y - entityPos.Y) +
                        (cameraPosition.Z - entityPos.Z) * (cameraPosition.Z - entityPos.Z)
                    );
                    Debug.Info($"HandleInteraction: Started scaling on handle {handleId}, scale {currentScale}");
                }
                // Rotation
                else if (handleId >= 8 && handleId <= 10 && draggedEntity != uint.MaxValue)
                {
                    IsDragging = true;
                    selectedHandle = handleId;
                    lastMousePos = mousePos;

                    rotationAxis = handleId switch
                    {
                        8 => new float3(-1, 0, 0),
                        9 => new float3(0, -1, 0),
                        _ => new float3(0, 0, -1)
                    };

                    var (planeU, planeV) = GetRingBasis(handleId);
                    float3 rayDir = GetMouseRayDir(mousePos, camTransform, GetMainCameraComponent());
                    float angle = ComputeRingAngle(cameraPosition, rayDir, entityTransform!.position, rotationAxis, planeU, planeV);
                    if (!float.IsNaN(angle)) lastAngle = angle;

                    Debug.Info($"HandleInteraction: Started rotation on handle {selectedHandle}");
                }
            }

            // Dragging
            if (IsDragging && mouseDown)
            {
                // Calculate delta from LAST frame
                float2 delta = new float2(
                    mousePos.X - lastMousePos.X,
                    -(mousePos.Y - lastMousePos.Y)
                );

                if ((delta.X != 0 || delta.Y != 0) && draggedEntity != uint.MaxValue)
                {
                    Transform? transform = W.GetComponent<Transform>(draggedEntity);
                    if (transform == null) return;

                    // Translation
                    if (selectedHandle >= 1 && selectedHandle <= 3)
                    {
                        float3 movement = GetAxisMovement(delta);

                        // Scale by distance to camera for natural feel
                        float distanceScale = distanceToCamera * 0.002f;
                        movement = new float3(
                            movement.X * distanceScale,
                            movement.Y * distanceScale,
                            movement.Z * distanceScale
                        );

                        currentPosition = new float3(
                            currentPosition.X + movement.X,
                            currentPosition.Y + movement.Y,
                            currentPosition.Z + movement.Z
                        );

                        transform.position = currentPosition;
                        PropertiesRefreshSystem.OnFieldChanged(draggedEntity, typeof(Transform).FullName);
                        RenderPipeline.Instance?.ShowHandles(transform.position, EditorSettings.Instance!.EditorHandleScale);

                        // Update distance as we move
                        distanceToCamera = math.sqrt(
                            (cameraPosition.X - currentPosition.X) * (cameraPosition.X - currentPosition.X) +
                            (cameraPosition.Y - currentPosition.Y) * (cameraPosition.Y - currentPosition.Y) +
                            (cameraPosition.Z - currentPosition.Z) * (cameraPosition.Z - currentPosition.Z)
                        );
                    }
                    // Scaling
                    else if (selectedHandle >= 5 && selectedHandle <= 7)
                    {
                        // Use the combined delta for scaling (both X and Y movement contribute)
                        float scaleDelta = (delta.X + delta.Y) * 0.01f;

                        // Start from CURRENT scale, not original
                        float3 newScale = currentScale;
                        switch (selectedHandle)
                        {
                            case 5: // X Scale (orange square)
                                newScale.X = math.max(0.01f, currentScale.X + scaleDelta);
                                break;
                            case 6: // Y Scale (green square)
                                newScale.Y = math.max(0.01f, currentScale.Y + scaleDelta);
                                break;
                            case 7: // Z Scale (blue square)
                                newScale.Z = math.max(0.01f, currentScale.Z + scaleDelta);
                                break;
                        }

                        transform.scaling = newScale;
                        PropertiesRefreshSystem.OnFieldChanged(draggedEntity, typeof(Transform).FullName);
                        currentScale = newScale; // Update current scale for next frame

                        if (scaleDelta != 0)
                            Debug.Info($"HandleInteraction: Scaling {selectedHandle}: {newScale}");
                    }
                    // Rotation
                    else if (selectedHandle >= 8 && selectedHandle <= 10)
                    {
                        Camera? cam = GetMainCameraComponent();
                        if (cam != null)
                        {
                            var (planeU, planeV) = GetRingBasis(selectedHandle);
                            float3 rayDir = GetMouseRayDir(mousePos, GetMainCamera()!, cam);
                            float currentAngle = ComputeRingAngle(cameraPosition, rayDir, transform.position, rotationAxis, planeU, planeV);

                            if (!float.IsNaN(currentAngle))
                            {
                                float angleDelta = currentAngle - lastAngle;
                                if (angleDelta > 3.14159265f) angleDelta -= 6.28318530718f;
                                else if (angleDelta < -3.14159265f) angleDelta += 6.28318530718f;

                                // Verify against your actual Quaternion API (name/order may differ)
                                float4 deltaRot = Quaternion.CreateFromAxisAngle(rotationAxis, angleDelta);
                                transform.rotation = Quaternion.Normalize(Quaternion.Multiply(deltaRot, transform.rotation));

                                PropertiesRefreshSystem.OnFieldChanged(draggedEntity, typeof(Transform).FullName);
                                lastAngle = currentAngle;
                            }
                        }
                    }
                }

                lastMousePos = mousePos;
            }

            // Stop dragging
            if (!mouseDown && IsDragging)
            {
                Debug.Info($"HandleInteraction: Stopped dragging handle {selectedHandle}");
                IsDragging = false;
                selectedHandle = 0;
            }
        }

        private static float3 GetAxisMovement(float2 delta)
        {
            float3 axisDirection = selectedHandle switch
            {
                1 => new float3(1, 0, 0),
                2 => new float3(0, 1, 0),
                3 => new float3(0, 0, 1),
                _ => float3.Zero
            };

            // Project the axis direction onto camera's screen plane
            float2 screenAxis = new float2(
                Vector.Dot(axisDirection, cameraRight),
                Vector.Dot(axisDirection, cameraUp)
            );

            // Normalize the screen axis to get direction
            float screenAxisLength = math.sqrt(screenAxis.X * screenAxis.X + screenAxis.Y * screenAxis.Y);
            if (screenAxisLength > 0.001f)
                screenAxis = new float2(screenAxis.X / screenAxisLength, screenAxis.Y / screenAxisLength);
            else screenAxis = selectedHandle == 1 ? new float2(1, 0) : new float2(0, 1);
            float projection = delta.X * screenAxis.X + delta.Y * screenAxis.Y;

            // Convert back to world movement
            return new float3(
                axisDirection.X * projection,
                axisDirection.Y * projection,
                axisDirection.Z * projection
            );
        }

        private static Transform? GetMainCamera()
        {
            foreach (var (_, transform, camera) in W.QueryData<Transform, Camera>())
                if (camera.isActive)
                    return transform;
            return null;
        }

        private static (float3 u, float3 v) GetRingBasis(uint handleId) => handleId switch
        {
            8 => (new float3(0, 1, 0), new float3(0, 0, 1)),
            9 => (new float3(0, 0, 1), new float3(1, 0, 0)),
            10 => (new float3(1, 0, 0), new float3(0, 1, 0)),
            _ => (new float3(1, 0, 0), new float3(0, 1, 0))
        };

        private static Camera? GetMainCameraComponent()
        {
            foreach (var (_, camera) in W.QueryData<Camera>())
                if (camera.isActive) return camera;
            return null;
        }

        private static float3 GetMouseRayDir(float2 mousePos, Transform camTransform, Camera? camera)
        {
            if (camera == null || RenderPipeline.Instance == null) return new float3(0, 0, 1);

            int screenW = RenderPipeline.Instance.EmbeddedWidth;
            int screenH = RenderPipeline.Instance.EmbeddedHeight;
            if (screenW < 1 || screenH < 1) return new float3(0, 0, 1);

            float aspect = screenW / (float)screenH;
            float camScreenDist = DivisionEngine.Systems.CameraSystem.FovToScreenDistance(camera);

            float flippedY = screenH - 1 - mousePos.Y;
            float uvX = mousePos.X / screenW * 2f - 1f;
            float uvY = flippedY / screenH * 2f - 1f;

            float px = uvX * aspect * camScreenDist;
            float py = uvY * camScreenDist;

            float3 dir = new float3(
                camTransform.Forward.X + camTransform.Right.X * px + camTransform.Up.X * py,
                camTransform.Forward.Y + camTransform.Right.Y * px + camTransform.Up.Y * py,
                camTransform.Forward.Z + camTransform.Right.Z * px + camTransform.Up.Z * py);

            float len = math.sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
            if (len > 0.0001f) dir = new float3(dir.X / len, dir.Y / len, dir.Z / len);
            return dir;
        }

        private static float ComputeRingAngle(float3 rayOrigin, float3 rayDir, float3 center, float3 axis, float3 planeU, float3 planeV)
        {
            float denom = Vector.Dot(rayDir, axis);
            if (math.abs(denom) < 0.0001f) return float.NaN;

            float3 toCenter = new float3(center.X - rayOrigin.X, center.Y - rayOrigin.Y, center.Z - rayOrigin.Z);
            float t = Vector.Dot(toCenter, axis) / denom;
            if (t < 0f) return float.NaN;

            float3 hitPoint = new float3(rayOrigin.X + rayDir.X * t, rayOrigin.Y + rayDir.Y * t, rayOrigin.Z + rayDir.Z * t);
            float3 rel = new float3(hitPoint.X - center.X, hitPoint.Y - center.Y, hitPoint.Z - center.Z);

            float u = Vector.Dot(rel, planeU);
            float v = Vector.Dot(rel, planeV);
            return math.atan2(v, u);
        }
    }
}
