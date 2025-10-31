using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Shared;

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
        private SimpleSignalAwaiter _awaiter;

        public RockPaperScissorsGameLogic(IReadOnlyList<ulong> playerIds, TimeSpan timeout)
        {
            if (playerIds == null) throw new ArgumentNullException(nameof(playerIds));
            if (playerIds.Count != 2) throw new ArgumentException("Exactly two players are required.", nameof(playerIds));

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
                return false;
            }

            if (!_choices.ContainsKey(playerId))
            {
                return false;
            }

            if (_choices[playerId].HasValue)
            {
                return false;
            }

            _choices[playerId] = hand;

            if (AllHandsSubmitted())
            {
                _awaiter?.OnSignal();
            }

            return true;
        }

        /// <summary>
        /// Waits for all submissions (or the timeout) and returns the computed result.
        /// </summary>
        public async Task<RpsResult> RunAsync(CancellationToken ct = default)
        {
            if (AllHandsSubmitted())
            {
                return BuildResult();
            }

            var awaiter = new SimpleSignalAwaiter(_timeout, ct);
            _awaiter = awaiter;
            try
            {
                var completed = await awaiter.WaitAsync();
                if (!completed)
                {
                    FillMissingHands();
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                FillMissingHands();
            }
            finally
            {
                _awaiter = null;
                awaiter.Dispose();
            }

            return BuildResult();
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
                    _choices[key] = HandExtensions.RandomHand();
                }
            }
        }

        private RpsResult BuildResult()
        {
            var player1Id = _playerIds[0];
            var player2Id = _playerIds[1];
            var player1Hand = _choices[player1Id] ?? HandExtensions.RandomHand();
            var player2Hand = _choices[player2Id] ?? HandExtensions.RandomHand();
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
