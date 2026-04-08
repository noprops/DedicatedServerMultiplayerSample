using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
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
        private string currentTicketId;

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
            Debug.Log($"[MM-TIMING][ClientMatchmaker] MatchmakeAsync start t={Time.realtimeSinceStartup:F3}");
            IsMatchmaking = true;

            NetworkSceneManager.OnEventCompletedDelegateHandler loadHandler = null;
            if (networkManager != null && networkManager.SceneManager != null)
            {
                loadHandler = (sceneName, mode, clientsCompleted, clientsTimedOut) =>
                {
                    if (sceneName == "game" &&
                        clientsCompleted != null &&
                        clientsCompleted.Contains(networkManager.LocalClientId))
                    {
                        Debug.Log($"[MM-PROBE][ClientMatchmaker] SceneLoadCompleted scene=game t={Time.realtimeSinceStartup:F3}");
                        Debug.Log($"[MM-TIMING][ClientMatchmaker] SceneLoadCompleted scene=game t={Time.realtimeSinceStartup:F3}");
                    }
                };
                networkManager.SceneManager.OnLoadEventCompleted += loadHandler;
            }

            try
            {
                NotifyState(ClientConnectionState.SearchingMatch);

                matchmakerCancellationSource = new CancellationTokenSource();
                Debug.Log("[ClientMatchmaker] ====================================");

                var authId = AuthenticationWrapper.PlayerId;

                playerProperties ??= new Dictionary<string, object>();
                ticketAttributes ??= new Dictionary<string, object>();
                sessionMetadata ??= new Dictionary<string, object>();

                var connectionBytes = MatchmakingPayloadConverter.ToConnectionPayload(connectionPayload, authId);
                networkManager.NetworkConfig.ConnectionData = connectionBytes;

                Debug.Log("[ClientMatchmaker] STEP 2: Searching for match...");
                Debug.Log($"[ClientMatchmaker] Creating low-level matchmaking ticket with queue: {queueName}");

                var ticketOptions = new CreateTicketOptions(queueName, ticketAttributes);
                var ticketPlayer = new Player(authId, playerProperties);
                var ticketResponse = await MatchmakerService.Instance.CreateTicketAsync(
                    new List<Player> { ticketPlayer },
                    ticketOptions);

                currentTicketId = ticketResponse?.Id;
                Debug.Log($"[ClientMatchmaker] Ticket created: {currentTicketId}");
                var assignment = await WaitForIpPortAssignmentAsync(currentTicketId, matchmakerCancellationSource.Token);
                Debug.Log($"[MM-TIMING][ClientMatchmaker] Ticket resolved t={Time.realtimeSinceStartup:F3}");

                if (assignment == null || string.IsNullOrWhiteSpace(assignment.Ip) || !assignment.Port.HasValue)
                {
                    Debug.LogError("[ClientMatchmaker] Failed to resolve ip/port assignment");
                    NotifyState(ClientConnectionState.Failed);
                    return MatchResult.Failed;
                }

                Debug.Log($"[ClientMatchmaker] ✓ Match found via ticket: {assignment.MatchId}");
                Debug.Log($"[ClientMatchmaker] Proceeding to connect with assignment {assignment.Ip}:{assignment.Port.Value}");
                NotifyState(ClientConnectionState.MatchFound);

                Debug.Log("[ClientMatchmaker] STEP 3: Preparing connection...");
                var transport = networkManager.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    Debug.LogError("[ClientMatchmaker] UnityTransport component not found!");
                    NotifyState(ClientConnectionState.Failed);
                    return MatchResult.Failed;
                }

                transport.SetConnectionData(assignment.Ip, (ushort)assignment.Port.Value);
                networkManager.NetworkConfig.NetworkTransport = transport;

                if (!networkManager.StartClient())
                {
                    Debug.LogError("[ClientMatchmaker] Failed to start client with ip/port assignment.");
                    NotifyState(ClientConnectionState.Failed);
                    return MatchResult.Failed;
                }

                Debug.Log("[ClientMatchmaker] STEP 4: Connecting to server...");
                NotifyState(ClientConnectionState.ConnectingToServer);

                Debug.Log($"[MM-TIMING][ClientMatchmaker] WaitForConnection begin t={Time.realtimeSinceStartup:F3}");
                bool connected = await WaitForConnection();
                if (!connected)
                {
                    Debug.LogError("[ClientMatchmaker] Connection timeout");
                    Debug.Log($"[MM-TIMING][ClientMatchmaker] WaitForConnection timeout t={Time.realtimeSinceStartup:F3}");
                    NotifyState(ClientConnectionState.Failed);
                    return MatchResult.Timeout;
                }

                Debug.Log("[ClientMatchmaker] ✓ Connected to server");
                Debug.Log($"[MM-TIMING][ClientMatchmaker] WaitForConnection success t={Time.realtimeSinceStartup:F3}");
                Debug.Log($"[MM-PROBE][ClientMatchmaker] Connected t={Time.realtimeSinceStartup:F3}");
                NotifyState(ClientConnectionState.Connected);

                Debug.Log("[ClientMatchmaker] STEP 5: Waiting for scene sync...");
                Debug.Log("[ClientMatchmaker] ✓ Ready for game");

                Debug.Log("[ClientMatchmaker] ========== MATCHMAKING COMPLETE ==========");
                return MatchResult.Success;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[ClientMatchmaker] Matchmaking cancelled by user");
                NotifyState(ClientConnectionState.Cancelled);
                return MatchResult.UserCancelled;
            }
            catch (AggregateException ae) when (ae.InnerExceptions.Any(inner => inner is OperationCanceledException or TaskCanceledException))
            {
                Debug.Log("[ClientMatchmaker] Matchmaking cancelled by user (aggregate)");
                NotifyState(ClientConnectionState.Cancelled);
                return MatchResult.UserCancelled;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClientMatchmaker] Matchmaking failed: {e.Message}");
                NotifyState(ClientConnectionState.Failed);
                return MatchResult.Failed;
            }
            finally
            {
                await DeleteCurrentTicketIfNeededAsync();

                if (loadHandler != null && networkManager != null && networkManager.SceneManager != null)
                {
                    networkManager.SceneManager.OnLoadEventCompleted -= loadHandler;
                }
                IsMatchmaking = false;
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

            await DeleteCurrentTicketIfNeededAsync();

            try
            {
                await LeaveCurrentSessionAsync();
            }
            catch (TaskCanceledException)
            {
                Debug.Log("[ClientMatchmaker] Leave session cancelled.");
            }
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

        public async Task ShutdownAsync()
        {
            try
            {
                await DeleteCurrentTicketIfNeededAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClientMatchmaker] Shutdown cleanup failed: {e.Message}");
            }

            matchmakerCancellationSource?.Cancel();
            matchmakerCancellationSource?.Dispose();
            matchmakerCancellationSource = null;
        }

        private async Task<IpPortAssignment> WaitForIpPortAssignmentAsync(string ticketId, CancellationToken cancellationToken)
        {
            const float ticketPollingTimeoutSeconds = 90f;
            float elapsed = 0f;

            while (elapsed < ticketPollingTimeoutSeconds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ticketStatus = await MatchmakerService.Instance.GetTicketAsync(ticketId);
                if (ticketStatus?.Type == typeof(IpPortAssignment) && ticketStatus.Value is IpPortAssignment ipPortAssignment)
                {
                    switch (ipPortAssignment.Status)
                    {
                        case IpPortAssignment.StatusOptions.Found:
                            return ipPortAssignment;
                        case IpPortAssignment.StatusOptions.Failed:
                            throw new Exception($"Ticket Failed: {ipPortAssignment.Message ?? "AllocationError"}");
                        case IpPortAssignment.StatusOptions.Timeout:
                            throw new Exception($"Ticket Timeout: {ipPortAssignment.Message ?? "Timeout"}");
                    }
                }

                if (ticketStatus?.Type == typeof(NoneAssignment) && ticketStatus.Value is NoneAssignment noneAssignment)
                {
                    switch (noneAssignment.Status)
                    {
                        case NoneAssignment.StatusOptions.InProgress:
                            break;
                        case NoneAssignment.StatusOptions.Failed:
                            throw new Exception($"Ticket Failed: {noneAssignment.Message ?? "AllocationError"}");
                        case NoneAssignment.StatusOptions.Timeout:
                            throw new Exception($"Ticket Timeout: {noneAssignment.Message ?? "Timeout"}");
                        case NoneAssignment.StatusOptions.Found:
                            throw new Exception("Ticket Failed: Assignment should have changed.");
                    }
                }

                await Task.Delay(1000, cancellationToken);
                elapsed += 1f;
            }

            throw new Exception("Ticket Timeout: polling exceeded 90 seconds");
        }

        private async Task DeleteCurrentTicketIfNeededAsync()
        {
            if (string.IsNullOrWhiteSpace(currentTicketId))
            {
                return;
            }

            try
            {
                await MatchmakerService.Instance.DeleteTicketAsync(currentTicketId);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ClientMatchmaker] Failed to delete ticket {currentTicketId}: {e.Message}");
            }
            finally
            {
                currentTicketId = null;
            }
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
