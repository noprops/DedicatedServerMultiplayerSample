#if UNITY_SERVER || ENABLE_UCS_SERVER
using UnityEngine;

namespace MultiplayerServicesTest.Server
{
    /// <summary>
    /// サーバー専用のパフォーマンス最適化設定
    /// </summary>
    public static class ServerPerformanceOptimizer
    {
        public static void Initialize()
        {
            Debug.Log("[ServerPerformanceOptimizer] Initializing server performance optimizations");

            // フレームレート制限
            Application.targetFrameRate = 15;  // サーバーは15 FPSで十分
            Debug.Log($"  - Target FPS: {Application.targetFrameRate}");

            // 物理演算の最小化
            DisablePhysics();

            // グラフィック関連の無効化
            DisableGraphics();

            // ガベージコレクションの最適化
            OptimizeGarbageCollection();

            Debug.Log("[ServerPerformanceOptimizer] All optimizations applied");
        }

        private static void DisablePhysics()
        {
            // 物理演算の更新頻度を最小に（完全に無効化はできないが最小限に）
            Time.fixedDeltaTime = 1.0f;  // 1秒に1回のみ更新（実質的に無効化）
            Physics.simulationMode = SimulationMode.Script;  // スクリプト制御のみ

            // 2D物理演算も最小化
            Physics2D.simulationMode = SimulationMode2D.Script;

            // 物理演算のスリープ閾値を最大に
            Physics.defaultSolverIterations = 1;  // ソルバーイテレーションを最小に
            Physics.defaultSolverVelocityIterations = 1;
            Physics.sleepThreshold = 10.0f;  // スリープしやすくする

            Debug.Log("  - Physics: Disabled (Script mode only)");
            Debug.Log($"  - Fixed Delta Time: {Time.fixedDeltaTime}s");
            Debug.Log($"  - Physics Simulation Mode: {Physics.simulationMode}");
            Debug.Log($"  - Physics2D Simulation Mode: {Physics2D.simulationMode}");
        }

        private static void DisableGraphics()
        {
            // VSync無効化
            QualitySettings.vSyncCount = 0;

            // すべての品質設定を最低に
            QualitySettings.SetQualityLevel(0, true);

            // 個別のグラフィック設定を無効化
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.shadowDistance = 0;
            QualitySettings.shadowCascades = 0;

            QualitySettings.pixelLightCount = 0;
            QualitySettings.globalTextureMipmapLimit = 3;  // テクスチャ品質を最低に
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
            QualitySettings.antiAliasing = 0;

            QualitySettings.softParticles = false;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.billboardsFaceCameraPosition = false;

            // LODバイアスを最低品質に
            QualitySettings.lodBias = 0.3f;
            QualitySettings.maximumLODLevel = 0;

            // パーティクルレイキャストバジェットを0に
            QualitySettings.particleRaycastBudget = 0;

            Debug.Log("  - Graphics: All features disabled");
            Debug.Log($"  - Quality Level: {QualitySettings.GetQualityLevel()}");
            Debug.Log($"  - VSync: {QualitySettings.vSyncCount}");
        }

        private static void OptimizeGarbageCollection()
        {
            // ガベージコレクションを増分モードに
            UnityEngine.Scripting.GarbageCollector.GCMode =
                UnityEngine.Scripting.GarbageCollector.Mode.Enabled;

            // メモリ使用量の最適化
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
            System.GC.Collect();

            Debug.Log("  - Garbage Collection: Optimized");
        }

        /// <summary>
        /// 物理演算を手動で実行する必要がある場合のみ使用
        /// </summary>
        public static void SimulatePhysicsOnce()
        {
            if (Physics.simulationMode == SimulationMode.Script)
            {
                Physics.Simulate(Time.fixedDeltaTime);
            }
        }

        /// <summary>
        /// 2D物理演算を手動で実行する必要がある場合のみ使用
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