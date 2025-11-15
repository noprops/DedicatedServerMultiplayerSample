#if UNITY_SERVER || ENABLE_UCS_SERVER
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Infrastructure
{
    /// <summary>
    /// Applies headless/dedicated-server friendly performance settings.
    /// </summary>
    public static class ServerPerformanceOptimizer
    {
        public static void Initialize()
        {
            Debug.Log("[ServerPerformanceOptimizer] Initializing server performance optimizations");

            // Cap rendering.
            Application.targetFrameRate = 15;  // Servers only need a low tick rate.
            Debug.Log($"  - Target FPS: {Application.targetFrameRate}");

            DisablePhysics();
            DisableGraphics();
            OptimizeGarbageCollection();

            Debug.Log("[ServerPerformanceOptimizer] All optimizations applied");
        }

        private static void DisablePhysics()
        {
            // Minimize physics work (cannot fully disable, but run rarely).
            Time.fixedDeltaTime = 1.0f;
            Physics.simulationMode = SimulationMode.Script;

            Physics2D.simulationMode = SimulationMode2D.Script;

            Physics.defaultSolverIterations = 1;
            Physics.defaultSolverVelocityIterations = 1;
            Physics.sleepThreshold = 10.0f;

            Debug.Log("  - Physics: Disabled (Script mode only)");
            Debug.Log($"  - Fixed Delta Time: {Time.fixedDeltaTime}s");
            Debug.Log($"  - Physics Simulation Mode: {Physics.simulationMode}");
            Debug.Log($"  - Physics2D Simulation Mode: {Physics2D.simulationMode}");
        }

        private static void DisableGraphics()
        {
            QualitySettings.vSyncCount = 0;
            QualitySettings.SetQualityLevel(0, true);

            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.shadowDistance = 0;
            QualitySettings.shadowCascades = 0;

            QualitySettings.pixelLightCount = 0;
            QualitySettings.globalTextureMipmapLimit = 3;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.antiAliasing = 0;

            QualitySettings.softParticles = false;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.billboardsFaceCameraPosition = false;

            QualitySettings.lodBias = 0.3f;
            QualitySettings.maximumLODLevel = 0;

            QualitySettings.particleRaycastBudget = 0;

            Debug.Log("  - Graphics: All features disabled");
            Debug.Log($"  - Quality Level: {QualitySettings.GetQualityLevel()}");
            Debug.Log($"  - VSync: {QualitySettings.vSyncCount}");
        }

        private static void OptimizeGarbageCollection()
        {
            UnityEngine.Scripting.GarbageCollector.GCMode =
                UnityEngine.Scripting.GarbageCollector.Mode.Enabled;

            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();

            Debug.Log("  - Garbage Collection: Optimized");
        }

        /// <summary>
        /// Manual physics simulation helper for rare cases.
        /// </summary>
        public static void SimulatePhysicsOnce()
        {
            if (Physics.simulationMode == SimulationMode.Script)
            {
                Physics.Simulate(Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// Manual 2D physics simulation helper for rare cases.
        /// </summary>
        public static void SimulatePhysics2DOnce()
        {
            if (Physics2D.simulationMode == SimulationMode2D.Script)
            {
                Physics2D.Simulate(Time.fixedDeltaTime);
            }
        }
    }
}
#endif
