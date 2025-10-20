using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Shared;
using DedicatedServerMultiplayerSample.Samples.Shared;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    public class RockPaperScissorsUI : MonoBehaviour
    {
        private const string LOADING_SCENE_NAME = "loading";

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
        [SerializeField] private CountdownButton okButton;

        [Header("Status")]
        [SerializeField] private TMP_Text statusText;

        [Header("Player Names")]
        [SerializeField] private TMP_Text myNameText;
        [SerializeField] private TMP_Text opponentNameText;

        private bool m_HasSubmitted;
        private CancellationTokenSource lifecycleCts;

        private async void Start()
        {
            Debug.Log("[RockPaperScissorsUI] Start - Beginning game flow");

            lifecycleCts = new CancellationTokenSource();
            var token = lifecycleCts.Token;

            try
            {
                await RunLifecycleAsync(token);
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[RockPaperScissorsUI] Flow cancelled (likely due to disable).");
            }
            catch (Exception e)
            {
                Debug.LogError($"[RockPaperScissorsUI] Game flow error: {e.Message}");
                SetStatus("An error occurred. Returning to menu...");
                DisconnectAndReturnToMenu();
            }
            finally
            {
                lifecycleCts?.Dispose();
                lifecycleCts = null;
            }
        }

        private void OnDisable()
        {
            lifecycleCts?.Cancel();
            lifecycleCts?.Dispose();
            lifecycleCts = null;
        }

        private async Task RunLifecycleAsync(CancellationToken token)
        {
            Debug.Log("[RockPaperScissorsUI] STEP 1: Initializing UI");
            resultPanel.SetActive(false);
            choicePanel.SetActive(false);
            m_HasSubmitted = false;

            Debug.Log("[RockPaperScissorsUI] STEP 2: Waiting for both players to join");
            SetStatus("Waiting for game to start...");

            var playerNames = await WaitForPlayerNamesAsync(token);
            DisplayPlayerNames(playerNames);
            choicePanel.SetActive(true);

            Debug.Log("[RockPaperScissorsUI] STEP 3: Waiting for player choice");
            var playerChoice = await WaitForPlayerChoiceAsync(token);
            Debug.Log($"[RockPaperScissorsUI] Player selected: {playerChoice}");
            await SubmitChoiceAsync(playerChoice);

            Debug.Log("[RockPaperScissorsUI] STEP 4: Waiting for game result");
            SetStatus("Waiting for opponent...");
            var (myHand, opponentHand, result) = await WaitForGameResultAsync(token);
            DisplayGameResult(myHand, opponentHand, result);

            Debug.Log("[RockPaperScissorsUI] STEP 5: Waiting for OK button");
            var okResult = await okButton.RunAsync(10f);
            if (okResult == CountdownCompletionReason.Timeout)
            {
                Debug.Log("[RockPaperScissorsUI] OK button countdown elapsed - proceeding automatically.");
            }

            Debug.Log("[RockPaperScissorsUI] STEP 6: Returning to loading scene");
            DisconnectAndReturnToMenu();
        }

        private Task<Dictionary<ulong, string>> WaitForPlayerNamesAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<Dictionary<ulong, string>>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(Dictionary<ulong, string> names)
            {
                PlayerInfoBroadcaster.OnPlayerNamesReceived -= Handler;
                tcs.TrySetResult(names);
            }

            PlayerInfoBroadcaster.OnPlayerNamesReceived += Handler;

            if (token.CanBeCanceled)
            {
                token.Register(() =>
                {
                    PlayerInfoBroadcaster.OnPlayerNamesReceived -= Handler;
                    tcs.TrySetCanceled(token);
                });
            }

            return tcs.Task;
        }

        private Task<(Hand myHand, Hand opponentHand, GameResult result)> WaitForGameResultAsync(CancellationToken token)
        {
            var tcs = new TaskCompletionSource<(Hand myHand, Hand opponentHand, GameResult result)>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(Hand myHand, Hand opponentHand, GameResult gameResult)
            {
                RockPaperScissorsGame.OnGameResultReceived -= Handler;
                tcs.TrySetResult((myHand, opponentHand, gameResult));
            }

            RockPaperScissorsGame.OnGameResultReceived += Handler;

            if (token.CanBeCanceled)
            {
                token.Register(() =>
                {
                    RockPaperScissorsGame.OnGameResultReceived -= Handler;
                    tcs.TrySetCanceled(token);
                });
            }

            return tcs.Task;
        }

        private async Task<Hand> WaitForPlayerChoiceAsync(CancellationToken token)
        {
            SetChoiceButtonsInteractable(true);
            SetStatus("Choose your hand!");

            int choiceIndex = await UIHelper.WaitForChoiceAsync(token, rockButton, paperButton, scissorsButton);

            return choiceIndex switch
            {
                0 => Hand.Rock,
                1 => Hand.Paper,
                2 => Hand.Scissors,
                _ => Hand.Rock
            };
        }

        private async Task SubmitChoiceAsync(Hand choice)
        {
            if (m_HasSubmitted)
            {
                Debug.Log("[RockPaperScissorsUI] Already submitted");
                return;
            }

            m_HasSubmitted = true;
            SetChoiceButtonsInteractable(false);

            await Task.Run(() => RockPaperScissorsGame.Instance?.SubmitChoiceServerRpc(choice));
        }

        private void DisplayPlayerNames(Dictionary<ulong, string> playerNames)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient)
            {
                return;
            }

            ulong myId = NetworkManager.Singleton.LocalClientId;

            if (playerNames.ContainsKey(myId))
            {
                myNameText.text = $"You: {playerNames[myId]}";
            }

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

        private void DisplayGameResult(Hand myHand, Hand opponentHand, GameResult result)
        {
            choicePanel.SetActive(false);
            resultPanel.SetActive(true);

            myChoiceText.text = $"You chose: {myHand}";
            opponentChoiceText.text = $"Opponent chose: {opponentHand}";
            resultText.text = result switch
            {
                GameResult.Win => "You Win!",
                GameResult.Lose => "You Lose!",
                GameResult.Draw => "Draw!",
                _ => "Result Unknown"
            };
        }

        private void SetChoiceButtonsInteractable(bool interactable)
        {
            rockButton.interactable = interactable;
            paperButton.interactable = interactable;
            scissorsButton.interactable = interactable;
        }

        private void SetStatus(string message)
        {
            statusText.text = message;
        }

        private void DisconnectAndReturnToMenu()
        {
            SetStatus("Returning to menu...");

            var manager = ClientSingleton.Instance?.GameManager;
            if (manager != null)
            {
                manager.Disconnect();
            }
            else if (SceneManager.GetActiveScene().name != LOADING_SCENE_NAME)
            {
                SceneManager.LoadScene(LOADING_SCENE_NAME);
            }
        }
    }
}
