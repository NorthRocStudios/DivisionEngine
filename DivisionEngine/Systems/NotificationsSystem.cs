//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the Division Engine License. See the LICENSE.txt file in the
// project root for full license terms.
//
using Avalonia.Threading;
using DivisionEngine.Editor.Tasks;
using DivisionEngine.Projects.Scripting;
using DivisionEngine.Systems;
using Material.Icons;

namespace DivisionEngine.Editor.Systems
{
    /// <summary>
    /// Manages system notifications for the Division Engine editor.
    /// <para>
    /// Bridges engine events (texture loading, script compilation) into the
    /// editor task manager, and forwards script compiler diagnostics into the
    /// console log stream so errors and warnings appear alongside runtime output.
    /// </para>
    /// </summary>
    internal class NotificationsSystem : SystemBase
    {
        private static EditorTask? textureLoadingTask;
        private static EditorTask? compilationTask;

        public override void AppStart()
        {
            TextureSystem.StartedLoadingTextureData += () =>
                Dispatcher.UIThread.Post(TextureSystem_StartedLoadingTextureData, DispatcherPriority.Normal);
            TextureSystem.UpdatedTextureData += () =>
                Dispatcher.UIThread.Post(TextureSystem_UpdatedTextureData, DispatcherPriority.Normal);

            ScriptCompilationPipeline.CompilationStarted += OnCompilationStarted;
            ScriptCompilationPipeline.CompilationCompleted += OnCompilationCompleted;
        }

        public override void EditorUpdate()
        {
            if (textureLoadingTask != null) EditorTaskManager.Update(textureLoadingTask.Id, TextureSystem.TextureLoadProgress);
        }

        public override void Unload()
        {
            ScriptCompilationPipeline.CompilationStarted -= OnCompilationStarted;
            ScriptCompilationPipeline.CompilationCompleted -= OnCompilationCompleted;

            if (compilationTask != null)
            {
                EditorTaskManager.Remove(compilationTask.Id);
                compilationTask = null;
            }
        }

        private void TextureSystem_UpdatedTextureData()
        {
            if (textureLoadingTask != null) EditorTaskManager.Remove(textureLoadingTask.Id);
            textureLoadingTask = null;
        }

        private void TextureSystem_StartedLoadingTextureData() =>
            textureLoadingTask = EditorTaskManager.Create("Texture System", "Loading project textures", 0f, MaterialIconKind.Texture);

        #region scriptCompilation

        private void OnCompilationStarted()
        {
            if (compilationTask != null) return;
            compilationTask = EditorTaskManager.Create(
                "Script Compilation",
                "Compiling project scripts",
                0f,
                MaterialIconKind.CodeBraces);
        }

        private void OnCompilationCompleted(ScriptCompileResult result)
        {
            if (compilationTask == null) return;
            EditorTaskManager.Remove(compilationTask.Id);
            compilationTask = null;
        }

        #endregion
    }
}
