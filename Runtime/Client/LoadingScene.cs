using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// ローディングシーンの管理クラス
    /// Unity Services初期化とプレイヤーデータのロードを担当
    /// </summary>
    public class LoadingScene : MonoBehaviour
    {
        // ========== Constants ==========
        private const string MENU_SCENE_NAME = "menu";

        // ========== Custom Tasks ==========
        private readonly List<Func<Task>> customTasks = new List<Func<Task>>();

        /// <summary>
        /// ローディングシーンがメニューへ遷移する直前に実行する処理を登録します。
        /// </summary>
        public void Register(Func<Task> task)
        {
            if (task == null) return;
            if (!customTasks.Contains(task))
            {
                customTasks.Add(task);
            }
        }

        /// <summary>
        /// Registerした処理を解除します。
        /// </summary>
        public void Unregister(Func<Task> task)
        {
            if (task == null) return;
            customTasks.Remove(task);
        }

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

            await RunCustomTasksAsync();
            
            // ================================================================
            // STEP 4: メニューシーンへ遷移
            // ================================================================
            Debug.Log("[LoadingScene] STEP 4: Loading menu scene...");

            // メニューシーンへ遷移
            SceneManager.LoadScene(MENU_SCENE_NAME);
            Debug.Log("[LoadingScene] ========== LOADING COMPLETE ==========");
        }

        private async Task RunCustomTasksAsync()
        {
            if (customTasks.Count == 0)
            {
                return;
            }

            foreach (var task in customTasks)
            {
                try
                {
                    var taskResult = task?.Invoke();
                    if (taskResult != null)
                    {
                        await taskResult;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[LoadingScene] Custom loading task failed: {e.Message}");
                }
            }
        }


        // ========== Cleanup ==========
        private void OnDestroy()
        {
            Debug.Log("[LoadingScene] LoadingScene destroyed");
            customTasks.Clear();
        }
    }
}
