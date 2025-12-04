using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Manages the loading scene by preparing services and client data before entering the menu.
    /// </summary>
    public class LoadingScene : MonoBehaviour
    {
        // ========== Constants ==========
        private const string MENU_SCENE_NAME = "menu";

        // ========== Custom Tasks ==========
        private readonly List<Func<Task>> customTasks = new List<Func<Task>>();

        /// <summary>
        /// Registers a task to run just before the loading scene transitions to the menu.
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
        /// Removes a previously registered task.
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
                // Execute the loading flow sequentially from top to bottom.
                await ExecuteLoadingSequence();
            }
            catch (Exception e)
            {
                Debug.LogError($"[LoadingScene] Loading sequence failed: {e.Message}");
                // No fallback here; handle an error screen if desired.
            }
        }

        // ========== Main Loading Sequence ==========
        /// <summary>
        /// Runs the main loading sequence in order before moving to the menu.
        /// </summary>
        private async Task ExecuteLoadingSequence()
        {
            Debug.Log("[LoadingScene] Starting loading sequence...");

            // ================================================================
            // STEP 1: Clean up any existing session
            // ================================================================
            Debug.Log("[LoadingScene] STEP 1: Cleaning up existing session if any...");

            // Leave the current session if one exists.
            var matchmaker = ClientSingleton.Instance?.Matchmaker;
            if (matchmaker != null)
            {
                await matchmaker.LeaveCurrentSessionAsync();
            }
            Debug.Log("[LoadingScene] ✓ Session cleanup complete");

            // ================================================================
            // STEP 2: Verify authentication state
            // ================================================================
            Debug.Log("[LoadingScene] STEP 2: Checking authentication status...");

            if (!AuthenticationWrapper.IsSignedIn)
            {
                Debug.LogError("[LoadingScene] Not authenticated. Should be done in ClientSingleton.");
                return;
            }

            Debug.Log($"[LoadingScene] ✓ Authenticated as: {AuthenticationWrapper.PlayerId}");

            // ================================================================
            // STEP 3: Execute registered custom tasks
            // ================================================================
            await RunCustomTasksAsync();

            // ================================================================
            // STEP 4: Transition to the menu scene
            // ================================================================
            Debug.Log("[LoadingScene] STEP 4: Loading menu scene...");

            SceneManager.LoadScene(MENU_SCENE_NAME);
            Debug.Log("[LoadingScene] ========== LOADING COMPLETE ==========");
        }

        /// <summary>
        /// Executes any custom tasks that were registered by other components.
        /// </summary>
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
