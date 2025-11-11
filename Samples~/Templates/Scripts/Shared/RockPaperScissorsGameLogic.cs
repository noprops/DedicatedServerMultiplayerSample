using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Pure rock-paper-scissors rules engine; handles round orchestration and outcome calculation.
    /// </summary>
    public sealed class RockPaperScissorsGameLogic
    {
        private readonly ulong[] _playerIds;
        private readonly Dictionary<ulong, Hand?> _choices = new();
        private readonly TimeSpan _timeout;
        private Action _signalAllHandsReady;

        /// <summary>
        /// Creates a new coordinator for a single round.
        /// </summary>
        /// <param name="playerIds">Exactly two participants (human or CPU) taking part in the round.</param>
        /// <param name="timeout">Maximum duration to wait for both hands before auto-filling remaining choices.</param>
        public RockPaperScissorsGameLogic(IReadOnlyList<ulong> playerIds, TimeSpan timeout = default)
        {
            if (playerIds == null) throw new ArgumentNullException(nameof(playerIds));
            if (playerIds.Count != 2) throw new ArgumentException("Exactly two players are required.", nameof(playerIds));
            if (timeout == default)
            {
                timeout = TimeSpan.FromSeconds(30);
            }

            _playerIds = new ulong[playerIds.Count];
            for (var i = 0; i < playerIds.Count; i++)
            {
                var id = playerIds[i];
                _playerIds[i] = id;
                _choices[id] = null;
            }

            _timeout = timeout;
        }

        /// <summary>
        /// Registers a hand for the provided player identifier.
        /// </summary>
        public bool SubmitHand(ulong playerId, Hand hand)
        {
            if (hand == Hand.None)
            {
                Debug.LogWarningFormat("[RpsLogic] Rejecting Hand.None from {0}", playerId);
                return false;
            }

            if (!_choices.ContainsKey(playerId))
            {
                Debug.LogWarningFormat("[RpsLogic] Rejecting unknown player {0}", playerId);
                return false;
            }

            if (_choices[playerId].HasValue)
            {
                Debug.LogWarningFormat("[RpsLogic] Rejecting duplicate submission from {0}", playerId);
                return false;
            }

            _choices[playerId] = hand;
            Debug.LogFormat("[RpsLogic] Accepted hand {0} from {1}", hand, playerId);

            if (AllHandsSubmitted())
            {
                Debug.Log("[RpsLogic] All hands submitted; signalling awaiter");
                _signalAllHandsReady?.Invoke();
            }

            return true;
        }

        /// <summary>
        /// Waits for all submissions (or the timeout) and returns the computed result.
        /// </summary>
        public async Task<RpsResult> RunAsync(CancellationToken ct = default)
        {
            Debug.Log("[RpsLogic] RunAsync started");
            await WaitForHandsOrTimeoutAsync(ct);
            return BuildResult();
        }

        private async Task WaitForHandsOrTimeoutAsync(CancellationToken ct)
        {
            if (AllHandsSubmitted())
            {
                Debug.Log("[RpsLogic] All hands already present; resolving immediately");
                return;
            }

            using var awaiter = new SimpleSignalAwaiter(_timeout, ct);
            _signalAllHandsReady = awaiter.OnSignal;

            if (AllHandsSubmitted())
            {
                awaiter.OnSignal();
            }

            try
            {
                var completed = await awaiter.WaitAsync();
                if (!completed)
                {
                    Debug.Log("[RpsLogic] Timeout reached; filling missing hands");
                    FillMissingHands();
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Debug.LogWarning("[RpsLogic] Awaiter canceled; filling missing hands");
                FillMissingHands();
            }
            finally
            {
                _signalAllHandsReady = null;
            }
        }

        private bool AllHandsSubmitted()
        {
            foreach (var entry in _choices)
            {
                if (!entry.Value.HasValue)
                {
                    return false;
                }
            }

            return true;
        }

        private void FillMissingHands()
        {
            foreach (var key in _playerIds)
            {
                if (!_choices[key].HasValue)
                {
                    var generated = HandExtensions.RandomHand();
                    _choices[key] = generated;
                    Debug.LogFormat("[RpsLogic] Auto-filled hand {0} for player {1}", generated, key);
                }
            }
        }

        private RpsResult BuildResult()
        {
            var player1Id = _playerIds[0];
            var player2Id = _playerIds[1];
            var player1Hand = _choices[player1Id] ?? HandExtensions.RandomHand();
            var player2Hand = _choices[player2Id] ?? HandExtensions.RandomHand();
            Debug.LogFormat("[RpsLogic] Building result. P1={0}({1}), P2={2}({3})",
                player1Id, player1Hand, player2Id, player2Hand);
            return Resolve(player1Id, player2Id, player1Hand, player2Hand);
        }

        private RpsResult Resolve(ulong player1Id, ulong player2Id, Hand player1Hand, Hand player2Hand)
        {
            return new RpsResult
            {
                Player1Id = player1Id,
                Player2Id = player2Id,
                Player1Hand = player1Hand,
                Player2Hand = player2Hand,
                Player1Outcome = DetermineOutcome(player1Hand, player2Hand),
                Player2Outcome = DetermineOutcome(player2Hand, player1Hand)
            };
        }

        private RoundOutcome DetermineOutcome(Hand me, Hand opponent)
        {
            if (me == opponent)
            {
                return RoundOutcome.Draw;
            }

            return ((me == Hand.Rock && opponent == Hand.Scissors) ||
                    (me == Hand.Paper && opponent == Hand.Rock) ||
                    (me == Hand.Scissors && opponent == Hand.Paper))
                ? RoundOutcome.Win
                : RoundOutcome.Lose;
        }
    }
}
