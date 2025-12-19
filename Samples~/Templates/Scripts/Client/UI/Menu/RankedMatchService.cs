using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Client.Data;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Thin wrapper around ClientMatchmaker for the ranked queue (start/cancel only).
    /// </summary>
    internal sealed class RankedMatchService
    {
        private readonly ClientMatchmaker _matchmaker;
        private readonly ClientData _clientData;
        private readonly string _queueName;
        private bool _isMatchmaking;

        public RankedMatchService(
            ClientMatchmaker matchmaker,
            ClientData clientData,
            string queueName = "competitive-queue")
        {
            _matchmaker = matchmaker;
            _clientData = clientData;
            _queueName = queueName;
        }

        public bool CanOperate => _matchmaker != null;
        public bool IsMatchmaking => _isMatchmaking;
        public event Action<ClientConnectionState> StateChanged;

        public async Task<MatchResult> StartMatchAsync()
        {
            if (_matchmaker == null)
            {
                throw new InvalidOperationException("Matchmaker is not initialized.");
            }

            if (_isMatchmaking)
            {
                throw new InvalidOperationException("Ranked match is already running.");
            }

            var playerProps = _clientData?.GetPlayerProperties();
            var ticketAttributes = _clientData?.GetTicketAttributes();
            var connectionPayload = _clientData?.GetConnectionData();
            var sessionProps = _clientData?.GetSessionProperties();
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
                Debug.LogWarning($"[RankedMatchService] Matchmake failed: {ex.Message}");
                return MatchResult.Failed;
            }
            finally
            {
                _matchmaker.StateChanged -= HandleStateChanged;
                _isMatchmaking = false;
            }
        }

        public async Task CancelMatchAsync()
        {
            if (_matchmaker == null || !_isMatchmaking)
            {
                return;
            }

            try
            {
                await _matchmaker.CancelMatchmakingAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RankedMatchService] Cancel failed: {ex.Message}");
            }
            finally
            {
                _isMatchmaking = false;
            }
        }

    }
}
