using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerServicesTest.Client
{
    /// <summary>
    /// ローディングシーンの管理クラス
    /// Unity Services初期化とプレイヤーデータのロードを担当
    /// </summary>
    public class LoadingScene : MonoBehaviour
    {
        // ========== Constants ==========
        private const string MENU_SCENE_NAME = "menu";

        // ========== Unity Lifecycle ==========
        private async void Start()
        {
            Debug.Log("[LoadingScene] ========== LOADING SCENE START ==========");

            try
            {
                // 上から下へ一直線に処理
                await ExecuteLoadingSequence();
            }
            catch (Exception e)
            {
                Debug.LogError($"[LoadingScene] Loading sequence failed: {e.Message}");
                // エラーが起きても何もしない（エラー画面を表示する場合はここで処理）
            }
        }

        // ========== Main Loading Sequence ==========
        /// <summary>
        /// ローディングシーケンスのメイン処理（上から下へ一直線）
        /// </summary>
        private async Task ExecuteLoadingSequence()
        {
            Debug.Log("[LoadingScene] Starting loading sequence...");

            // ================================================================
            // STEP 1: 既存セッションのクリーンアップ
            // ================================================================
            Debug.Log("[LoadingScene] STEP 1: Cleaning up existing session if any...");

            // GameManagerがある場合は既存セッションを離脱
            await ClientSingleton.Instance.GameManager.LeaveCurrentSessionAsync();
            Debug.Log("[LoadingScene] ✓ Session cleanup complete");

            // ================================================================
            // STEP 2: 認証状態の確認
            // ================================================================
            Debug.Log("[LoadingScene] STEP 2: Checking authentication status...");

            if (!AuthenticationWrapper.IsSignedIn)
            {
                Debug.LogError("[LoadingScene] Not authenticated. Should be done in ClientSingleton.");
                return;
            }

            Debug.Log($"[LoadingScene] ✓ Authenticated as: {AuthenticationWrapper.PlayerId}");
            Debug.Log($"[LoadingScene] ✓ Player name: {AuthenticationWrapper.PlayerName}");

            // ================================================================
            // STEP 3: プレイヤーデータのロード（Cloud Save）
            // ================================================================
            Debug.Log("[LoadingScene] STEP 3: Loading player data...");

            bool dataLoaded = await LoadPlayerDataAsync();
            if (!dataLoaded)
            {
                Debug.LogWarning("[LoadingScene] Failed to load player data, using defaults");
                // プレイヤーデータのロードに失敗しても続行
            }
            else
            {
                Debug.Log("[LoadingScene] ✓ Player data loaded");
            }

            // ================================================================
            // STEP 4: メニューシーンへ遷移
            // ================================================================
            Debug.Log("[LoadingScene] STEP 4: Loading menu scene...");

            // メニューシーンへ遷移
            SceneManager.LoadScene(MENU_SCENE_NAME);
            Debug.Log("[LoadingScene] ========== LOADING COMPLETE ==========");
        }

        // ========== Player Data Loading ==========
        /// <summary>
        /// プレイヤーデータをCloud Saveからロード
        /// </summary>
        private async Task<bool> LoadPlayerDataAsync()
        {
            try
            {
                // TODO: Cloud Saveからプレイヤーデータをロード
                // 現在は未実装なので、仮の処理として少し待つだけ
                await Task.Delay(100);

                Debug.Log("[LoadingScene] Player data loading is not yet implemented");

                // 将来的な実装例：
                // var cloudSaveData = await CloudSaveService.Instance.Data.LoadAsync();
                // PlayerDataManager.Instance.UpdateLocalData(cloudSaveData);

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[LoadingScene] Failed to load player data: {e.Message}");
                return false;
            }
        }


        // ========== Cleanup ==========
        private void OnDestroy()
        {
            Debug.Log("[LoadingScene] LoadingScene destroyed");
        }
    }
}