//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
namespace DivisionEngine
{
    /// <summary>
    /// The base class all systems inherit from.
    /// </summary>
    public abstract class SystemBase
    {
        /// <summary>
        /// Priority of this system (lower number = executed first).
        /// Default is 0.
        /// </summary>
        public virtual int Priority => 0;

        /// <summary>
        /// Called once when the editor or player application is launched.
        /// </summary>
        public virtual void AppStart() { }

        /// <summary>
        /// Called once when the world is run.
        /// </summary>
        public virtual void Awake() { }

        /// <summary>
        /// Called once every frame.
        /// </summary>
        public virtual void Update() { }

        /// <summary>
        /// Called once every frame even if not in play mode (only in the editor).
        /// </summary>
        public virtual void EditorUpdate() { }

        /// <summary>
        /// Called once every frame after Update loop has completed.
        /// </summary>
        public virtual void FixedUpdate() { }

        /// <summary>
        /// Called once when the world is stopped or unloaded.
        /// </summary>
        public virtual void Unload() { }

        /// <summary>
        /// Called once when the editor or player application is about to exit.
        /// </summary>
        public virtual void AppExit() { }
        
        /// <summary>
        /// Called before every render thread execution step.
        /// </summary>
        public virtual void Render() { }
    }
}
