using System;
using System.Collections.Generic;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Encapsulates the rock-paper-scissors rules and round bookkeeping independent of transport concerns.
    /// </summary>
    public sealed class RockPaperScissorsGameLogic
    {
        private readonly List<ulong> _players;
        private readonly Dictionary<ulong, Hand> _choices;

        public RockPaperScissorsGameLogic(IEnumerable<ulong> playerIds)
        {
            _players = playerIds != null ? new List<ulong>(playerIds) : new List<ulong>();
            _choices = new Dictionary<ulong, Hand>();
        }

        public IReadOnlyList<ulong> Players => _players;
        public int PlayerCount => _players.Count;
        public int ChoiceCount => _choices.Count;
        public bool IsComplete => _players.Count > 0 && _choices.Count >= _players.Count;

        public bool ContainsPlayer(ulong playerId) => _players.Contains(playerId);

        public bool HasChoice(ulong playerId) => _choices.ContainsKey(playerId);

        public bool TryGetChoice(ulong playerId, out Hand choice) => _choices.TryGetValue(playerId, out choice);

        public bool SubmitChoice(ulong playerId, Hand choice)
        {
            if (choice == Hand.None || !ContainsPlayer(playerId) || HasChoice(playerId))
            {
                return false;
            }

            _choices[playerId] = choice;
            return true;
        }

        public bool ForceChoice(ulong playerId, Hand choice)
        {
            if (!ContainsPlayer(playerId))
            {
                return false;
            }

            _choices[playerId] = choice;
            return true;
        }

        public IEnumerable<ulong> PlayersWithoutChoice()
        {
            foreach (var id in _players)
            {
                if (!_choices.ContainsKey(id))
                {
                    yield return id;
                }
            }
        }

        public bool TryResolve(out RpsResult result)
        {
            if (_players.Count < 2)
            {
                result = default;
                return false;
            }

            var p1 = _players[0];
            var p2 = _players[1];

            var h1 = _choices.TryGetValue(p1, out var choice1) ? choice1 : Hand.None;
            var h2 = _choices.TryGetValue(p2, out var choice2) ? choice2 : Hand.None;

            result = new RpsResult
            {
                P1 = p1,
                P2 = p2,
                H1 = h1,
                H2 = h2,
                P1Outcome = (byte)DetermineOutcome(h1, h2),
                P2Outcome = (byte)DetermineOutcome(h2, h1)
            };

            return true;
        }

        public static RoundOutcome DetermineOutcome(Hand myHand, Hand opponentHand)
        {
            if (myHand == opponentHand)
            {
                return RoundOutcome.Draw;
            }

            return ((myHand == Hand.Rock && opponentHand == Hand.Scissors) ||
                    (myHand == Hand.Paper && opponentHand == Hand.Rock) ||
                    (myHand == Hand.Scissors && opponentHand == Hand.Paper))
                ? RoundOutcome.Win
                : RoundOutcome.Lose;
        }
    }
}
