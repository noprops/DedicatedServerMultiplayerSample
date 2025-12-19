using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Client.Data;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Coordinates the friend-match flow by creating/joining Lobbies,
    /// keeping the host lobby alive via heartbeats, and driving Matchmaker calls.
    /// </summary>
    internal sealed class FriendMatchService : IDisposable
    {
        private const int MaxPlayers = 2;
        private const float HeartbeatIntervalSeconds = 15f;

        private readonly ClientMatchmaker _matchmaker;
        private readonly ClientData _clientData;
        private readonly string _queueName;

        private Lobby _currentLobby;
        private CancellationTokenSource _heartbeatCts;
        private bool _isMatchmaking;

        public event Action<ClientConnectionState> StateChanged;

        public FriendMatchService(
            ClientMatchmaker matchmaker,
            ClientData clientData,
            string queueName = "casual-queue")
        {
            _matchmaker = matchmaker;
            _clientData = clientData;
            _queueName = queueName;
        }

        public async Task<string> CreateRoomAsync()
        {
            await LeaveLobbyAsync();

            var lobbyName = $"friend-{Guid.NewGuid():N}";
            var options = new CreateLobbyOptions
            {
                IsPrivate = true,
                Player = BuildPlayerData()
            };

            _currentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, MaxPlayers, options);
            StartHeartbeat();
            return _currentLobby.LobbyCode;
        }

        public async Task JoinRoomAsync(string roomCode)
        {
            if (string.IsNullOrWhiteSpace(roomCode))
            {
                throw new ArgumentException("Room code cannot be empty", nameof(roomCode));
            }
            await LeaveLobbyAsync();

            var options = new JoinLobbyByCodeOptions
            {
                Player = BuildPlayerData()
            };

            _currentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(roomCode, options);
        }

        public async Task<MatchResult> StartMatchAsync()
        {
            if (_currentLobby == null)
            {
                throw new InvalidOperationException("Create or join a room before starting a match.");
            }

            if (_isMatchmaking)
            {
                throw new InvalidOperationException("Friend match is already running.");
            }

            var playerProps = _clientData?.GetPlayerProperties() ?? new Dictionary<string, object>();
            playerProps["roomCode"] = _currentLobby.LobbyCode;

            var ticketAttributes = _clientData?.GetTicketAttributes() ?? new Dictionary<string, object>();
            var connectionPayload = _clientData?.GetConnectionData() ?? new Dictionary<string, object>();
            var sessionProps = _clientData?.GetSessionProperties() ?? new Dictionary<string, object>();
            _isMatchmaking = true;

            void HandleStateChanged(ClientConnectionState state) => StateChanged?.Invoke(state);
            _matchmaker.StateChanged += HandleStateChanged;

            try
            {
                return await _matchmaker.MatchmakeAsync(
                    _queueName,
                    playerProps,
                    ticketAttributes,
                    connectionPayload,
                    sessionProps);
            }
            catch (OperationCanceledException)
            {
                return MatchResult.UserCancelled;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendMatchService] Matchmake failed: {ex.Message}");
                return MatchResult.Failed;
            }
            finally
            {
                _matchmaker.StateChanged -= HandleStateChanged;
                _isMatchmaking = false;
            }
        }

        public async Task CancelMatchmakingAsync()
        {
            if (_matchmaker != null && _isMatchmaking)
            {
                try
                {
                    await _matchmaker.CancelMatchmakingAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FriendMatchService] Cancel failed: {ex.Message}");
                }
                finally
                {
                    _isMatchmaking = false;
                }
            }

            await LeaveLobbyAsync();
        }

        public void Dispose()
        {
            StopHeartbeat();
            _ = LeaveLobbyAsync();
            _isMatchmaking = false;
        }

        private Player BuildPlayerData()
        {
            var playerName = _clientData?.PlayerName ?? "Player";
            var playerId = AuthenticationWrapper.PlayerId ?? string.Empty;

            return new Player
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "playerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Public, playerName) },
                    { "playerId", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerId) }
                }
            };
        }

        private async Task LeaveLobbyAsync()
        {
            if (_currentLobby == null)
            {
                StopHeartbeat();
                return;
            }

            var lobbyId = _currentLobby.Id;
            var playerId = AuthenticationWrapper.PlayerId;
            _currentLobby = null;
            StopHeartbeat();

            if (string.IsNullOrEmpty(lobbyId) || string.IsNullOrEmpty(playerId))
            {
                return;
            }

            try
            {
                await LobbyService.Instance.RemovePlayerAsync(lobbyId, playerId);
            }
            catch (LobbyServiceException ex)
            {
                Debug.LogWarning($"[FriendMatchService] Failed to leave lobby: {ex.Message}");
            }
        }

        private void StartHeartbeat()
        {
            StopHeartbeat();

            if (!IsHost())
            {
                return;
            }

            _heartbeatCts = new CancellationTokenSource();
            _ = HeartbeatLoopAsync(_heartbeatCts.Token);
        }

        private async Task HeartbeatLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _currentLobby != null && IsHost())
                {
                    await Task.Delay(TimeSpan.FromSeconds(HeartbeatIntervalSeconds), token);
                    await LobbyService.Instance.SendHeartbeatPingAsync(_currentLobby.Id);
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FriendMatchService] Heartbeat error: {ex.Message}");
            }
        }

        private void StopHeartbeat()
        {
            if (_heartbeatCts != null)
            {
                _heartbeatCts.Cancel();
                _heartbeatCts.Dispose();
                _heartbeatCts = null;
            }

        }

        private bool IsHost()
        {
            var playerId = AuthenticationWrapper.PlayerId;
            return _currentLobby != null && !string.IsNullOrEmpty(playerId) && _currentLobby.HostId == playerId;
        }

    }
}
