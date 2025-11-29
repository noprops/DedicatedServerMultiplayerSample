using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Matchmaking result types.
    /// </summary>
    public enum MatchResult
    {
        Success,
        UserCancelled,
        Failed,
        Timeout
    }

    /// <summary>
    /// Connection states reported during matchmaking.
    /// </summary>
    public enum ClientConnectionState
    {
        Idle,
        SearchingMatch,
        MatchFound,
        ConnectingToServer,
        Connected,
        Failed,
        Cancelling,
        Cancelled
    }

    /// <summary>
    /// Handles matchmaking start/cancel flow for the client.
    /// </summary>
    public sealed class ClientMatchmaker : IDisposable
    {
        private readonly NetworkManager networkManager;
        private readonly int maxPlayers;
        private ISession currentSession;
        private CancellationTokenSource matchmakerCancellationSource;

        public bool IsMatchmaking { get; private set; }
        public bool HasActiveSession => currentSession != null;

        public event Action<ClientConnectionState> StateChanged;

        public ClientMatchmaker(
            NetworkManager networkManager,
            int maxPlayers)
        {
            this.networkManager = networkManager;
            this.maxPlayers = Mathf.Max(1, maxPlayers);
        }

        public async Task<MatchResult> MatchmakeAsync(
            string queueName,
            Dictionary<string, object> playerProperties = null,
            Dictionary<string, object> ticketAttributes = null,
            Dictionary<string, object> connectionPayload = null,
            Dictionary<string, object> sessionMetadata = null)
        {
            if (IsMatchmaking)
            {
                Debug.LogWarning("[ClientMatchmaker] Already matchmaking");
                return MatchResult.Failed;
            }

            if (currentSession != null)
            {
                Debug.LogError($"[ClientMatchmaker] Session still exists (Code: {currentSession.Code}). Should be cleared in LoadingScene.");
                await LeaveCurrentSessionAsync();
            }

            Debug.Log("[ClientMatchmaker] ========== MATCHMAKING BEGIN ==========");
            IsMatchmaking = true;

            try
            {
                NotifyState(ClientConnectionState.SearchingMatch);

                matchmakerCancellationSource = new CancellationTokenSource();
                Debug.Log("[ClientMatchmaker] ====================================");

                var authId = AuthenticationWrapper.PlayerId;

                if (playerProperties == null)
                {
                    playerProperties = new Dictionary<string, object>();
                }

                if (ticketAttributes == null)
                {
                    ticketAttributes = new Dictionary<string, object>();
                }

                if (sessionMetadata == null)
                {
                    sessionMetadata = new Dictionary<string, object>();
                }

                var matchmakerOptions = new MatchmakerOptions
                {
                    QueueName = queueName,
                    PlayerProperties = MatchmakingPayloadConverter.ToPlayerProperties(playerProperties),
                    TicketAttributes = ticketAttributes
                };

                var connectionBytes = MatchmakingPayloadConverter.ToConnectionPayload(connectionPayload, authId);
                networkManager.NetworkConfig.ConnectionData = connectionBytes;

                var sessionOptions = new SessionOptions()
                {
                    MaxPlayers = maxPlayers,
                    SessionProperties = MatchmakingPayloadConverter.ToSessionProperties(sessionMetadata)
                }.WithDirectNetwork();

                Debug.Log("[ClientMatchmaker] STEP 2: Searching for match...");
                Debug.Log($"[ClientMatchmaker] Calling MatchmakeSessionAsync with queue: {queueName}");

                currentSession = await MultiplayerService.Instance.MatchmakeSessionAsync(
                    matchmakerOptions,
                    sessionOptions,
                    matchmakerCancellationSource.Token
                );

                if (currentSession == null)
                {
                    Debug.LogError("[ClientMatchmaker] Failed to find match");
                    NotifyState(ClientConnectionState.Failed);
                    IsMatchmaking = false;
                    return MatchResult.Failed;
                }

                Debug.Log($"[ClientMatchmaker] ✓ Match found: {currentSession.Code}");
                Debug.Log($"[ClientMatchmaker] Session details - Code: {currentSession.Code}, Id: {currentSession.Id}");
                Debug.Log($"[ClientMatchmaker] Proceeding to connect with Session Code={currentSession.Code}, Id={currentSession.Id}");
                NotifyState(ClientConnectionState.MatchFound);

                Debug.Log("[ClientMatchmaker] STEP 3: Preparing connection...");
                var transport = networkManager.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    Debug.LogError("[ClientMatchmaker] UnityTransport component not found!");
                    NotifyState(ClientConnectionState.Failed);
                    IsMatchmaking = false;
                    return MatchResult.Failed;
                }

                networkManager.NetworkConfig.NetworkTransport = transport;

                Debug.Log("[ClientMatchmaker] STEP 4: Connecting to server...");
                NotifyState(ClientConnectionState.ConnectingToServer);

                bool connected = await WaitForConnection();
                if (!connected)
                {
                    Debug.LogError("[ClientMatchmaker] Connection timeout");
                    NotifyState(ClientConnectionState.Failed);
                    IsMatchmaking = false;
                    return MatchResult.Timeout;
                }

                Debug.Log("[ClientMatchmaker] ✓ Connected to server");
                NotifyState(ClientConnectionState.Connected);

                Debug.Log("[ClientMatchmaker] STEP 5: Waiting for scene sync...");
                Debug.Log("[ClientMatchmaker] ✓ Ready for game");

                Debug.Log("[ClientMatchmaker] ========== MATCHMAKING COMPLETE ==========");
                IsMatchmaking = false;
                return MatchResult.Success;
            }
            catch (Exception e)
            {
                IsMatchmaking = false;

                if (e is OperationCanceledException or TaskCanceledException)
                {
                    Debug.Log("[ClientMatchmaker] Matchmaking cancelled by user");
                    NotifyState(ClientConnectionState.Cancelled);
                    return MatchResult.UserCancelled;
                }

                Debug.LogError($"[ClientMatchmaker] Matchmaking failed: {e.Message}");
                NotifyState(ClientConnectionState.Failed);
                return MatchResult.Failed;
            }
        }

        public async Task CancelMatchmakingAsync()
        {
            Debug.Log($"[ClientMatchmaker] Cancel requested - IsMatchmaking: {IsMatchmaking}");

            NotifyState(ClientConnectionState.Cancelling);

            if (matchmakerCancellationSource != null && !matchmakerCancellationSource.IsCancellationRequested)
            {
                matchmakerCancellationSource.Cancel();
                Debug.Log("[ClientMatchmaker] Matchmaking cancel token triggered");
            }

            await LeaveCurrentSessionAsync();
            IsMatchmaking = false;
            NotifyState(ClientConnectionState.Cancelled);
        }

        public async Task LeaveCurrentSessionAsync()
        {
            if (currentSession == null)
            {
                Debug.Log("[ClientMatchmaker] No current session to leave");
                return;
            }

            Debug.Log($"[ClientMatchmaker] Leaving current session (Code: {currentSession.Code})...");
            try
            {
                await currentSession.LeaveAsync();
                Debug.Log("[ClientMatchmaker] Successfully left session");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClientMatchmaker] Failed to leave session: {e.Message}");
            }
            finally
            {
                currentSession = null;
            }
        }

        public void Dispose()
        {
            matchmakerCancellationSource?.Cancel();
            matchmakerCancellationSource?.Dispose();
            matchmakerCancellationSource = null;
        }

        private async Task<bool> WaitForConnection()
        {
            const float timeout = 10f;
            float elapsed = 0f;
            int checkCount = 0;

            Debug.Log("[ClientMatchmaker] Waiting for connection...");

            while (elapsed < timeout)
            {
                if (networkManager.IsConnectedClient)
                {
                    return true;
                }

                await Task.Delay(100);
                elapsed += 0.1f;
                checkCount++;

                if (checkCount % 10 == 0)
                {
                    Debug.Log($"[ClientMatchmaker] Waiting... {elapsed:F1}s - IsClient: {networkManager.IsClient}, IsConnectedClient: {networkManager.IsConnectedClient}");
                    if (currentSession != null)
                    {
                        Debug.Log($"[ClientMatchmaker] Session still exists: Code={currentSession.Code}");
                    }
                    else
                    {
                        Debug.LogWarning("[ClientMatchmaker] Session is null!");
                    }
                }
            }

            Debug.LogError("[ClientMatchmaker] Failed to connect within timeout");
            Debug.Log($"[ClientMatchmaker] Final state - IsClient: {networkManager.IsClient}, IsConnectedClient: {networkManager.IsConnectedClient}, IsListening: {networkManager.IsListening}");

            if (currentSession != null)
            {
                Debug.LogWarning($"[ClientMatchmaker] Session still exists but not connected: Code={currentSession.Code}");
            }

            return false;
        }

        private void NotifyState(ClientConnectionState state)
        {
            StateChanged?.Invoke(state);
        }
    }
}
