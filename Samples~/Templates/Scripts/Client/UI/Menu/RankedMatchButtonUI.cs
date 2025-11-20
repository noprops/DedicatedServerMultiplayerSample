using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Client.UI.Common;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Handles the Online Match button flow (start/cancel matchmaking plus status text updates).
    /// </summary>
    public sealed class RankedMatchButtonUI : MonoBehaviour
    {
        private const string ReadyStatus = "Ready to start matchmaking";
        private const string DefaultQueueName = "competitive-queue";

        [SerializeField] private StartCancelUI controls;
        [SerializeField] private string queueName = DefaultQueueName;

        private ClientMatchmaker _matchmaker;

        public event Action CancelCompleted;

        private void Start()
        {
            _matchmaker = ClientSingleton.Instance?.Matchmaker;

            if (_matchmaker == null || controls == null)
            {
                controls?.SetStatus("Client not initialized");
                enabled = false;
                return;
            }

            controls.SetStatus(ReadyStatus);
            controls.ShowStartButton();
            controls.StartPressed += HandleStartPressed;
            controls.CancelPressed += HandleCancelPressed;
            NotifyReady();
        }

        private void OnDestroy()
        {
            controls.StartPressed -= HandleStartPressed;
            controls.CancelPressed -= HandleCancelPressed;
        }

        private MatchmakingPayload BuildPayload()
        {
            var source = ClientData.Instance;
            return new MatchmakingPayload(
                source?.GetPlayerProperties(),
                source?.GetTicketAttributes(),
                source?.GetConnectionData(),
                source?.GetSessionProperties());
        }

        private void NotifyReady() => CancelCompleted?.Invoke();

        private async void HandleStartPressed()
        {
            controls.ShowCancelButton();
            controls.SetStatus("Starting matchmaking...");

            var payload = BuildPayload();

            try
            {
                await ExecuteMatchmakingAsync(queueName, payload);
                controls.SetStatus("Connected!");
            }
            catch (OperationCanceledException)
            {
                controls.SetStatus("Cancelled by user");
            }
            catch (Exception ex)
            {
                controls.SetStatus($"Failed: {ex.Message}");
            }
            finally
            {
                controls.ShowStartButton();
                NotifyReady();
            }
        }

        private async void HandleCancelPressed()
        {
            controls.SetStatus("Cancelling...");
            try
            {
                await _matchmaker.CancelMatchmakingAsync();
                controls.SetStatus("Cancelled by user");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RankedMatchButtonUI] Cancel failed: {ex.Message}");
                controls.SetStatus($"Cancel failed: {ex.Message}");
            }
            finally
            {
                controls.ShowStartButton();
                NotifyReady();
            }
        }

        private async Task ExecuteMatchmakingAsync(
            string targetQueue,
            MatchmakingPayload payload)
        {
            controls.SetStatus("Searching for match...");

            void HandleStateChanged(ClientConnectionState state)
            {
                controls.SetStatus(state switch
                {
                    ClientConnectionState.SearchingMatch => "Searching for match...",
                    ClientConnectionState.MatchFound => "Match found! Preparing...",
                    ClientConnectionState.ConnectingToServer => "Connecting to server...",
                    ClientConnectionState.Connected => "Connected!",
                    ClientConnectionState.Cancelling => "Cancelling...",
                    ClientConnectionState.Cancelled => "Cancelled",
                    ClientConnectionState.Failed => "Connection failed",
                    _ => "Ready"
                });
            }

            _matchmaker.StateChanged += HandleStateChanged;

            try
            {
                await _matchmaker.MatchmakeAsync(
                        targetQueue,
                        payload.PlayerProperties,
                        payload.TicketAttributes,
                        payload.ConnectionPayload,
                        payload.SessionProperties)
                    ;
            }
            finally
            {
                _matchmaker.StateChanged -= HandleStateChanged;
            }
        }

        public sealed class MatchmakingPayload
        {
            public static MatchmakingPayload Empty { get; } = new MatchmakingPayload();

            public MatchmakingPayload(
                Dictionary<string, object> playerProperties = null,
                Dictionary<string, object> ticketAttributes = null,
                Dictionary<string, object> connectionPayload = null,
                Dictionary<string, object> sessionProperties = null)
            {
                PlayerProperties = playerProperties;
                TicketAttributes = ticketAttributes;
                ConnectionPayload = connectionPayload;
                SessionProperties = sessionProperties;
            }

            public Dictionary<string, object> PlayerProperties { get; }
            public Dictionary<string, object> TicketAttributes { get; }
            public Dictionary<string, object> ConnectionPayload { get; }
            public Dictionary<string, object> SessionProperties { get; }
        }
    }
}
