using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Shared;
using DedicatedServerMultiplayerSample.Samples.Shared;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    public class RockPaperScissorsUI : MonoBehaviour
    {
        // ========== Constants ==========
        private const string LOADING_SCENE_NAME = "loading";

        // ========== Serialized Fields ==========
        [Header("Panels")]
        [SerializeField] private GameObject choicePanel;
        [SerializeField] private GameObject resultPanel;

        [Header("Choice Buttons")]
        [SerializeField] private Button rockButton;
        [SerializeField] private Button paperButton;
        [SerializeField] private Button scissorsButton;

        [Header("Result UI")]
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private TMP_Text myChoiceText;
        [SerializeField] private TMP_Text opponentChoiceText;
        [SerializeField] private Button okButton;

        [Header("Status")]
        [SerializeField] private TMP_Text statusText;

        [Header("Player Names")]
        [SerializeField] private TMP_Text myNameText;
        [SerializeField] private TMP_Text opponentNameText;

        // ========== Private Fields ==========
        private bool m_HasSubmitted = false;

        // ========== TaskCompletionSources ==========
        private TaskCompletionSource<Dictionary<ulong, string>> m_PlayerNamesTcs;
        private TaskCompletionSource<(Hand myHand, Hand opponentHand, GameResult result)> m_GameResultTcs;
        // ========== Unity Lifecycle ==========

        private async void Start()
        {
            Debug.Log("[RockPaperScissorsUI] Start - Beginning game flow");

            try
            {
                // イベント購読
                SubscribeToEvents();

                // ========== メインフロー（完全に上から下へ） ==========

                // STEP 1: 初期化
                Debug.Log("[RockPaperScissorsUI] STEP 1: Initializing UI");
                resultPanel.SetActive(false);
                choicePanel.SetActive(true);

                // STEP 2: 両プレイヤーの参加を待つ
                Debug.Log("[RockPaperScissorsUI] STEP 2: Waiting for both players to join");
                UpdateStatus("Waiting for game to start...");
                var playerNames = await WaitForPlayerNames();
                DisplayPlayerNames(playerNames);

                // STEP 3: プレイヤーの選択を待つ
                Debug.Log("[RockPaperScissorsUI] STEP 3: Waiting for player choice");
                var playerChoice = await GetPlayerChoice();
                Debug.Log($"[RockPaperScissorsUI] Player selected: {playerChoice}");
                await SubmitChoice(playerChoice);

                // STEP 4: ゲーム結果を待つ
                Debug.Log("[RockPaperScissorsUI] STEP 4: Waiting for game result");
                UpdateStatus("Waiting for opponent...");
                var (myHand, opponentHand, result) = await WaitForGameResult();
                DisplayGameResult(myHand, opponentHand, result);

                // STEP 5: OKボタンを待つ
                Debug.Log("[RockPaperScissorsUI] STEP 5: Waiting for OK button");
                await WaitForOkButton();

                // STEP 6: クリーンアップしてloadingシーンへ
                Debug.Log("[RockPaperScissorsUI] STEP 6: Returning to loading scene");
                await ReturnToLoadingScene();
            }
            catch (Exception e)
            {
                Debug.LogError($"[RockPaperScissorsUI] Game flow error: {e.Message}");
                await HandleError();
            }
            finally
            {
                UnsubscribeFromEvents();
            }
        }

        // ========== Event Subscription ==========

        private void SubscribeToEvents()
        {
            RockPaperScissorsGame.OnStatusUpdated += OnStatusUpdated;
            PlayerInfoBroadcaster.OnPlayerNamesReceived += OnPlayerNamesReceived;
            RockPaperScissorsGame.OnGameResultReceived += OnGameResultReceived;
        }

        private void UnsubscribeFromEvents()
        {
            RockPaperScissorsGame.OnStatusUpdated -= OnStatusUpdated;
            PlayerInfoBroadcaster.OnPlayerNamesReceived -= OnPlayerNamesReceived;
            RockPaperScissorsGame.OnGameResultReceived -= OnGameResultReceived;
        }

        // ========== Event Handlers (TCSに結果を設定するだけ) ==========

        private void OnStatusUpdated(string message)
        {
            statusText.text = message;
        }

        private void OnPlayerNamesReceived(Dictionary<ulong, string> names)
        {
            m_PlayerNamesTcs?.TrySetResult(names);
        }

        private void OnGameResultReceived(Hand myHand, Hand opponentHand, GameResult result)
        {
            m_GameResultTcs?.TrySetResult((myHand, opponentHand, result));
        }

        // ========== Main Flow Methods (Called from Start) ==========

        private async Task<Dictionary<ulong, string>> WaitForPlayerNames()
        {
            m_PlayerNamesTcs = new TaskCompletionSource<Dictionary<ulong, string>>();
            return await m_PlayerNamesTcs.Task;
        }

        private void DisplayPlayerNames(Dictionary<ulong, string> playerNames)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
                return;

            ulong myId = NetworkManager.Singleton.LocalClientId;

            // 自分の名前を表示
            if (playerNames.ContainsKey(myId))
            {
                myNameText.text = $"You: {playerNames[myId]}";
            }

            // 相手の名前を表示
            foreach (var kvp in playerNames)
            {
                if (kvp.Key != myId)
                {
                    opponentNameText.text = $"Opponent: {kvp.Value}";
                    break;
                }
            }

            Debug.Log("[RockPaperScissorsUI] Both players ready - game can start!");
        }

        private async Task<Hand> GetPlayerChoice()
        {
            SetChoiceButtonsInteractable(true);
            UpdateStatus("Choose your hand!");

            // Use UIHelper.WaitForChoice to wait for one of three buttons
            int choiceIndex = await UIHelper.WaitForChoice(rockButton, paperButton, scissorsButton);

            Hand choice = choiceIndex switch
            {
                0 => Hand.Rock,
                1 => Hand.Paper,
                2 => Hand.Scissors,
                _ => Hand.Rock // Default fallback
            };

            return choice;
        }

        private async Task SubmitChoice(Hand choice)
        {
            if (m_HasSubmitted)
            {
                Debug.Log("[RockPaperScissorsUI] Already submitted");
                return;
            }

            m_HasSubmitted = true;
            SetChoiceButtonsInteractable(false);

            Debug.Log($"[RockPaperScissorsUI] Submitting choice: {choice}");
            UpdateStatus($"You selected: {GetHandText(choice)}");

            // サーバーに選択を送信
            var game = RockPaperScissorsGame.Instance;
            if (game != null)
            {
                game.SubmitChoiceServerRpc(choice);
            }
            else
            {
                Debug.LogError("[RockPaperScissorsUI] Game instance not available!");
            }

            await Task.Yield(); // 送信完了を待つ
        }

        private async Task<(Hand, Hand, GameResult)> WaitForGameResult()
        {
            m_GameResultTcs = new TaskCompletionSource<(Hand, Hand, GameResult)>();
            return await m_GameResultTcs.Task;
        }

        private void DisplayGameResult(Hand myHand, Hand opponentHand, GameResult result)
        {
            Debug.Log($"[RockPaperScissorsUI] Result: {result}, My: {myHand}, Opponent: {opponentHand}");

            // 選択パネルを非表示
            choicePanel.SetActive(false);

            // 結果パネルを表示
            resultPanel.SetActive(true);

            // 結果テキスト更新
            string resultMessage = result switch
            {
                GameResult.Win => "YOU WIN!",
                GameResult.Lose => "YOU LOSE",
                GameResult.Draw => "DRAW",
                _ => "ERROR"
            };
            resultText.text = resultMessage;

            // 色を設定
            resultText.color = result switch
            {
                GameResult.Win => Color.green,
                GameResult.Lose => Color.red,
                GameResult.Draw => Color.yellow,
                _ => Color.white
            };

            // 選択内容を表示
            myChoiceText.text = $"You: {GetHandText(myHand)}";
            opponentChoiceText.text = $"Opponent: {GetHandText(opponentHand)}";

            UpdateStatus("Game finished! Click OK to return to menu.");
        }

        private async Task WaitForOkButton()
        {
            await UIHelper.WaitForButton(
                okButton,
                true,
                onShow: () =>
                {
                    okButton.interactable = true;
                },
                onHide: () =>
                {
                    Debug.Log("[RockPaperScissorsUI] OK button pressed");
                    okButton.interactable = false;
                }
            );
        }

        private async Task ReturnToLoadingScene()
        {
            // ClientGameManagerのShutdownNetworkを呼び出す
            if (ClientSingleton.Instance != null && ClientSingleton.Instance.GameManager != null)
            {
                Debug.Log("[RockPaperScissorsUI] Calling ClientGameManager.ShutdownNetwork");
                ClientSingleton.Instance.GameManager.ShutdownNetwork();
            }
            else
            {
                // フォールバック：ClientGameManagerが見つからない場合は直接処理
                Debug.LogWarning("[RockPaperScissorsUI] ClientGameManager not found, using fallback");
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                {
                    NetworkManager.Singleton.Shutdown();
                }
                SceneManager.LoadScene(LOADING_SCENE_NAME);
            }

            await Task.Yield();
        }

        private async Task HandleError()
        {
            // エラーが起きてもloadingシーンに戻る
            if (ClientSingleton.Instance != null && ClientSingleton.Instance.GameManager != null)
            {
                ClientSingleton.Instance.GameManager.ShutdownNetwork();
            }
            else
            {
                // フォールバック
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
                {
                    NetworkManager.Singleton.Shutdown();
                }
                SceneManager.LoadScene(LOADING_SCENE_NAME);
            }

            await Task.Yield();
        }

        // ========== Helper Methods ==========

        private void UpdateStatus(string message)
        {
            statusText.text = message;
        }
        
        private void SetChoiceButtonsInteractable(bool interactable)
        {
            rockButton.interactable = interactable;
            paperButton.interactable = interactable;
            scissorsButton.interactable = interactable;
        }

        private string GetHandText(Hand hand)
        {
            return hand switch
            {
                Hand.Rock => "Rock",
                Hand.Paper => "Paper",
                Hand.Scissors => "Scissors",
                _ => "None"
            };
        }
    }
}
