using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Client.Data;
using DedicatedServerMultiplayerSample.Samples.Client.UI.Common;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Handles the ranked match UI flow by delegating start/cancel operations to RankedMatchService.
    /// </summary>
    public sealed class RankedMatchButtonUI : MonoBehaviour
    {
        private const string ReadyStatus = "Ready to start matchmaking";
        private const int CancelCooldownSeconds = 1;

        [SerializeField] private StartCancelUI controls;
        [SerializeField] private string queueName = "competitive-queue";
        [SerializeField] private ElapsedTimeTextUI matchmakingTimer;

        private RankedMatchService _matchService;
        private bool _userCancelled;

        /// <summary>
        /// Fired when matchmaking ends without success (cancelled/failed/timeout) and UI cooldown is complete.
        /// </summary>
        public event Action MatchmakingAborted;

        private void Start()
        {
            _matchService = new RankedMatchService(
                ClientSingleton.Instance?.Matchmaker,
                ClientData.Instance,
                queueName);

            if (!_matchService.CanOperate || controls == null)
            {
                controls?.SetStatus("Client not initialized");
                enabled = false;
                return;
            }

            if (matchmakingTimer != null)
            {
                matchmakingTimer.Format = "Matchmaking... {0}:{1:00}";
            }

            controls.SetStatus(ReadyStatus);
            controls.ShowStartButton();
            controls.StartPressed += HandleStartPressed;
            controls.CancelPressed += HandleCancelPressed;
            _matchService.StateChanged += HandleStateChanged;
        }

        private void OnDestroy()
        {
            if (controls != null)
            {
                controls.StartPressed -= HandleStartPressed;
                controls.CancelPressed -= HandleCancelPressed;
            }

            if (_matchService != null)
            {
                _matchService.StateChanged -= HandleStateChanged;
            }

            controls = null;
        }


        private async void HandleStartPressed()
        {
            _userCancelled = false;
            controls?.ShowCancelButton();
            matchmakingTimer?.StartTimer();

            MatchResult result = MatchResult.Failed;
            string waitStatus = "Cleaning up...";
            string finalStatus = "Failed. Try again.";
            try
            {
                result = await _matchService.StartMatchAsync();
                if (result == MatchResult.Success)
                {
                    controls?.SetStatus("Connected!");
                }
                else
                {
                    waitStatus = _userCancelled || result == MatchResult.UserCancelled
                        ? "Cancelling..."
                        : "Cleaning up...";
                    controls?.SetStatus(waitStatus);

                    finalStatus = _userCancelled || result == MatchResult.UserCancelled
                        ? "Cancelled by user"
                        : result == MatchResult.Timeout
                            ? "Timed out. Try again."
                            : "Failed. Try again.";
                }
            }
            catch (Exception ex)
            {
                waitStatus = "Cleaning up...";
                finalStatus = $"Failed: {ex.Message}";
                controls?.SetStatus(waitStatus);
            }
            finally
            {
                matchmakingTimer?.StopTimer();
                controls?.SetCancelInteractable(false);
            }

            if (result != MatchResult.Success)
            {
                await ShowStartButtonWithDelayAsync(CancelCooldownSeconds);
                controls?.SetStatus(finalStatus);
                MatchmakingAborted?.Invoke();
            }
        }

        private async void HandleCancelPressed()
        {
            _userCancelled = true;
            matchmakingTimer?.StopTimer();
            controls?.SetStatus("Cancelling...");
            await _matchService.CancelMatchAsync();
        }

        private void HandleStateChanged(ClientConnectionState state)
        {
            if (state == ClientConnectionState.SearchingMatch)
            {
                matchmakingTimer?.StartTimer();
                return;
            }

            matchmakingTimer?.StopTimer();

            controls?.SetStatus(state switch
            {
                ClientConnectionState.SearchingMatch => "Searching for match...",
                ClientConnectionState.MatchFound => "Match found! Preparing...",
                ClientConnectionState.ConnectingToServer => "Connecting to server...",
                ClientConnectionState.Connected => "Connected!",
                ClientConnectionState.Cancelling => "Cancelling...",
                ClientConnectionState.Cancelled => "Cancelled",
                ClientConnectionState.Failed => "Connection failed",
                _ => ReadyStatus
            });
        }

        private async Task ShowStartButtonWithDelayAsync(int delaySeconds)
        {
            if (delaySeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }
            controls?.ShowStartButton();
        }
    }
}
