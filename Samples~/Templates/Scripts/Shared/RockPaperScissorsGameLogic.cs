using System;
using System.Collections.Generic;
using DedicatedServerMultiplayerSample.Shared;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Pure rock-paper-scissors rules engine; computes round outcomes given player hands.
    /// </summary>
    public sealed class RockPaperScissorsGameLogic
    {
        private readonly ulong[] _playerIds;

        /// <summary>
        /// Creates a new coordinator for a single round.
        /// </summary>
        /// <param name="playerIds">Exactly two participants (human or CPU) taking part in the round.</param>
        public RockPaperScissorsGameLogic(IReadOnlyList<ulong> playerIds)
        {
            if (playerIds == null) throw new ArgumentNullException(nameof(playerIds));
            if (playerIds.Count != 2) throw new ArgumentException("Exactly two players are required.", nameof(playerIds));

            _playerIds = new ulong[playerIds.Count];
            for (var i = 0; i < playerIds.Count; i++)
            {
                _playerIds[i] = playerIds[i];
            }
        }

        /// <summary>
        /// Resolves a single round using the supplied choices; missing or Hand.None entries are randomised.
        /// </summary>
        public RpsResult ResolveRound(IReadOnlyDictionary<ulong, Hand> choices)
        {
            var player1Id = _playerIds[0];
            var player2Id = _playerIds[1];

            var player1Hand = choices != null &&
                              choices.TryGetValue(player1Id, out var p1Hand) &&
                              p1Hand != Hand.None
                ? p1Hand
                : HandExtensions.RandomHand();

            var player2Hand = choices != null &&
                              choices.TryGetValue(player2Id, out var p2Hand) &&
                              p2Hand != Hand.None
                ? p2Hand
                : HandExtensions.RandomHand();

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
