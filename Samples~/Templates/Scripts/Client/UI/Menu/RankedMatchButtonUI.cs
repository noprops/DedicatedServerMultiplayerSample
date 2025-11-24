using System;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
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

        [SerializeField] private StartCancelUI controls;
        [SerializeField] private string queueName = "competitive-queue";

        private RankedMatchService _matchService;

        public event Action CancelCompleted;

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
            controls?.ShowCancelButton();
            controls?.SetStatus("Starting matchmaking...");

            try
            {
                var result = await _matchService.StartMatchAsync();
                controls?.SetStatus(result switch
                {
                    MatchResult.Success => "Connected!",
                    MatchResult.UserCancelled => "Cancelled by user",
                    MatchResult.Timeout => "Timed out. Try again.",
                    _ => "Failed. Try again."
                });
            }
            catch (Exception ex)
            {
                controls?.SetStatus($"Failed: {ex.Message}");
            }
            finally
            {
                controls?.ShowStartButton();
            }
        }

        private async void HandleCancelPressed()
        {
            controls?.SetStatus("Cancelling...");
            await _matchService.CancelMatchAsync();
            controls?.ShowStartButton();
            controls?.SetStatus("Cancelled by user");
            CancelCompleted?.Invoke();
        }

        private void HandleStateChanged(ClientConnectionState state)
        {
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
    }
}
