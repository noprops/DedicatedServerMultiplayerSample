using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Assertions.Must;

namespace MultiplayerServicesTest.Client
{
    public class MatchmakingUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private string queueName = "default-queue";

        private ClientGameManager clientManager;
        private string lastResultMessage = "";  // 前回の結果メッセージを保持

        private async void Start()
        {
            // 初期化
            if (ClientSingleton.Instance != null)
            {
                clientManager = ClientSingleton.Instance.GameManager;
            }

            if (clientManager == null)
            {
                UpdateStatus("Client not initialized");
                return;
            }

            // メインループ実行
            await RunMainLoop();
        }

        /// <summary>
        /// メインループ（処理の流れが上から下に読める）
        /// </summary>
        private async Task RunMainLoop()
        {
            while (this != null && gameObject != null)  // オブジェクトが存在する限り
            {
                try
                {
                    // ========================================
                    // 1️⃣ 開始待機状態
                    // ========================================
                    // 前回の結果メッセージがあれば表示し続ける
                    if (!string.IsNullOrEmpty(lastResultMessage))
                    {
                        UpdateStatus(lastResultMessage);
                    }
                    else
                    {
                        UpdateStatus("Ready to start matchmaking");
                    }

                    await WaitForStartButton();

                    // Startボタンが押されたらメッセージをクリア
                    lastResultMessage = "";

                    // ========================================
                    // 2️⃣ マッチメイキング準備
                    // ========================================
                    UpdateStatus("Starting matchmaking...");

                    // ========================================
                    // 3️⃣ マッチメイキング実行
                    // ========================================
                    var result = await ExecuteMatchmaking();

                    // ========================================
                    // 4️⃣ 結果に応じた処理
                    // ========================================
                    switch (result)
                    {
                        case MatchResult.Success:
                            UpdateStatus("Connected!");
                            Debug.Log("[MatchmakingUI] Successfully connected, exiting loop");
                            return;  // ループ終了、シーン遷移へ

                        case MatchResult.UserCancelled:
                            lastResultMessage = "Cancelled by user";
                            UpdateStatus(lastResultMessage);
                            continue;

                        case MatchResult.Failed:
                            lastResultMessage = "Connection failed";
                            UpdateStatus(lastResultMessage);
                            continue;

                        case MatchResult.Timeout:
                            lastResultMessage = "Connection timeout";
                            UpdateStatus(lastResultMessage);
                            continue;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[MatchmakingUI] Error: {e.Message}");
                    lastResultMessage = "Error occurred";
                    UpdateStatus(lastResultMessage);
                    continue;
                }
            }
        }

        /// <summary>
        /// Startボタンが押されるまで待つ
        /// </summary>
        private async Task WaitForStartButton()
        {
            // Startボタンを表示、Cancelボタンは非表示
            startButton.gameObject.SetActive(true);
            cancelButton.gameObject.SetActive(false);
            startButton.interactable = true;

            await UIHelper.WaitForButton(
                startButton,
                true,
                onShow: () =>
                {
                    Debug.Log("[MatchmakingUI] Waiting for start button");
                },
                onHide: () =>
                {
                    Debug.Log("[MatchmakingUI] Start button pressed");
                    startButton.interactable = false;
                }
            );
        }

        /// <summary>
        /// キャンセルボタンが押されるのを待つ
        /// </summary>
        private async Task WaitForCancelButton()
        {
            startButton.gameObject.SetActive(false);
            cancelButton.gameObject.SetActive(true);
            cancelButton.interactable = true;

            await UIHelper.WaitForButton(
                cancelButton,
                true,
                onShow: () =>
                {
                    Debug.Log("[MatchmakingUI] Waiting for cancel button");
                },
                onHide: () =>
                {
                    Debug.Log("[MatchmakingUI] Cancel button pressed");
                    cancelButton.interactable = false;
                }
            );
        }

        /// <summary>
        /// マッチメイキング実行
        /// </summary>
        private async Task<MatchResult> ExecuteMatchmaking()
        {
            UpdateStatus("Searching for match...");

            // STEP 2: 状態変化監視の設定
            clientManager.OnStateChanged += UpdateStatusForState;

            try
            {
                // STEP 3: キャンセル処理を別タスクで開始
                var cancelTask = WaitForCancelButton();

                // STEP 4: マッチメイキング開始
                var matchTask = clientManager.MatchmakeAsync(queueName);

                // STEP 5: どちらか早い方を待つ
                var completedTask = await Task.WhenAny(cancelTask, matchTask);

                // STEP 6: 結果判定
                if (completedTask == cancelTask)
                {
                    // キャンセルされた
                    await clientManager.CancelMatchmakingAsync();
                    return MatchResult.UserCancelled;
                }
                else
                {
                    // マッチング結果を取得
                    var result = await matchTask;
                    return result;
                }
            }
            finally
            {
                if (cancelButton != null)
                {
                    cancelButton.interactable = false;
                }
                clientManager.OnStateChanged -= UpdateStatusForState;
            }
        }

        /// <summary>
        /// 状態に応じたステータステキストを更新
        /// </summary>
        private void UpdateStatusForState(ClientConnectionState state)
        {
            string message = state switch
            {
                ClientConnectionState.SearchingMatch => "Searching for match...",
                ClientConnectionState.MatchFound => "Match found! Preparing...",
                ClientConnectionState.ConnectingToServer => "Connecting to server...",
                ClientConnectionState.Connected => "Connected!",
                ClientConnectionState.Cancelling => "Cancelling...",
                ClientConnectionState.Cancelled => "Cancelled",
                ClientConnectionState.Failed => "Connection failed",
                _ => "Ready"
            };

            UpdateStatus(message);
        }

        /// <summary>
        /// ステータステキストを更新
        /// </summary>
        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
            Debug.Log($"[MatchmakingUI] {message}");
        }

        private void OnDestroy()
        {

            // ボタンリスナーをクリア
            startButton.onClick.RemoveAllListeners();
            cancelButton.onClick.RemoveAllListeners();
        }
    }
}