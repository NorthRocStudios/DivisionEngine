//
// Copyright (c) 2025-2026 Rex Woodfield and Division Engine contributors
//
// This file is part of Division Engine and is subject to the terms
// of the MIT License. See the LICENSE file in the project root
// for full license terms.
//
using DivisionEngine.Rendering;
using static DivisionEngine.Debug;

namespace DivisionEngine.Systems
{
    /// <summary>
    /// Manages the timing and frame rate of the engine.
    /// </summary>
    public class TimeSystem : SystemBase
    {
        public const int FPSFramesMeasured = 20;

        private double timeBetweenFrames; // in seconds
        private int fpsFrameCounter;

        /// <summary>
        /// Current delta time in the world (time between frames) in seconds.
        /// </summary>
        public static double DeltaTime => RenderPipeline.DeltaTime;

        /// <summary>
        /// Current delta time in the world (time between frames) in seconds, floating point.
        /// </summary>
        public static float DeltaTimeF => (float)DeltaTime;

        /// <summary>
        /// Current time in the world.
        /// </summary>
        public static double Time => RenderPipeline.Time;

        /// <summary>
        /// Current time in the world, floating point.
        /// </summary>
        public static float TimeF => (float)Time;

        /// <summary>
        /// Number of frames elapsed since world began.
        /// </summary>
        public static int FrameCount { get; private set; }

        /// <summary>
        /// Frames per second, recorded every 20 frames.
        /// </summary>
        public static float FPS { get; private set; }

        public override void Awake()
        {
            fpsFrameCounter = 0;
            FrameCount = 0;
            timeBetweenFrames = 0;
            Info("Time measurement began in current world");
        }

        public override void Render()
        {
            FrameCount++;
            fpsFrameCounter++;
            timeBetweenFrames += DeltaTime;
            if (fpsFrameCounter > FPSFramesMeasured)
            {
                FPS = fpsFrameCounter / (float)timeBetweenFrames;
                timeBetweenFrames = 0;
                fpsFrameCounter = 0;
            }
        }
    }
}
