using System;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Pure rock-paper-scissors rules engine that tracks two participants and raises an event when both hands are known.
    /// </summary>
    public sealed class RockPaperScissorsGameLogic
    {
        private readonly ulong[] _players = new ulong[2];
        private readonly Hand[] _choices = new Hand[2];
        private readonly bool[] _hasChoice = new bool[2];

        private bool _roundActive;
        private bool _resultReady;
        private RpsResult _result;

        /// <summary>
        /// Raised once when both hands are available and the round has been resolved.
        /// </summary>
        public event Action<RpsResult> RoundResolved;

        /// <summary>
        /// Starts a new round for the supplied participants. Existing choices and results are cleared.
        /// </summary>
        public void InitializeRound(ulong player0, ulong player1)
        {
            _players[0] = player0;
            _players[1] = player1;

            _choices[0] = Hand.None;
            _choices[1] = Hand.None;
            _hasChoice[0] = false;
            _hasChoice[1] = false;

            _resultReady = false;
            _roundActive = true;
        }

        /// <summary>
        /// Attempts to record a hand for the given slot (0 or 1). Returns false when the slot already chose or the hand is invalid.
        /// </summary>
        public bool TrySubmit(int slot, Hand hand)
        {
            if (!_roundActive || slot < 0 || slot > 1 || hand is Hand.None || _hasChoice[slot])
            {
                return false;
            }

            _choices[slot] = ValidateHand(hand);
            _hasChoice[slot] = true;

            if (_hasChoice[0] && _hasChoice[1])
            {
                Resolve();
            }

            return true;
        }

        /// <summary>
        /// Returns true when the specified slot has already provided a hand.
        /// </summary>
        public bool HasChoice(int slot) => slot is 0 or 1 && _hasChoice[slot];

        /// <summary>
        /// Retrieves the hand recorded for the given slot when available.
        /// </summary>
        public bool TryGetChoice(int slot, out Hand choice)
        {
            if (slot is < 0 or > 1)
            {
                choice = Hand.None;
                return false;
            }

            choice = _choices[slot];
            return _hasChoice[slot];
        }

        /// <summary>
        /// Assigns random hands to any slots that have not yet chosen and resolves the round if possible.
        /// </summary>
        public void AssignIfMissing(Func<Hand> handProvider)
        {
            if (handProvider == null)
            {
                throw new ArgumentNullException(nameof(handProvider));
            }

            if (!_roundActive)
            {
                return;
            }

            for (var slot = 0; slot < 2; slot++)
            {
                if (_hasChoice[slot])
                {
                    continue;
                }

                var generated = ValidateHand(handProvider());
                _choices[slot] = generated;
                _hasChoice[slot] = true;
            }

            if (_hasChoice[0] && _hasChoice[1])
            {
                Resolve();
            }
        }

        /// <summary>
        /// Attempts to retrieve the recorded hand for the provided player identifier.
        /// </summary>
        public bool TryGetChoice(ulong playerId, out Hand choice)
        {
            if (TryGetSlot(playerId, out var slot))
            {
                return TryGetChoice(slot, out choice);
            }

            choice = Hand.None;
            return false;
        }

        /// <summary>
        /// Attempts to map the supplied player identifier to a slot index.
        /// </summary>
        public bool TryGetSlot(ulong playerId, out int slot)
        {
            if (_players[0] == playerId)
            {
                slot = 0;
                return true;
            }

            if (_players[1] == playerId)
            {
                slot = 1;
                return true;
            }

            slot = -1;
            return false;
        }

        /// <summary>
        /// Returns true when the supplied player participates in the current round.
        /// </summary>
        public bool ContainsPlayer(ulong playerId) => TryGetSlot(playerId, out _);

        /// <summary>
        /// Returns the player identifier registered for the supplied slot.
        /// </summary>
        public ulong GetPlayerId(int slot)
        {
            return slot switch
            {
                0 => _players[0],
                1 => _players[1],
                _ => throw new ArgumentOutOfRangeException(nameof(slot))
            };
        }

        /// <summary>
        /// Retrieves the resolved result, if available.
        /// </summary>
        public bool TryGetResult(out RpsResult result)
        {
            result = _result;
            return _resultReady;
        }

        /// <summary>
        /// Indicates whether the round is still collecting hands.
        /// </summary>
        public bool IsRoundActive => _roundActive;

        private void Resolve()
        {
            if (_resultReady)
            {
                return;
            }

            var hand0 = _choices[0] == Hand.None ? Hand.Rock : _choices[0];
            var hand1 = _choices[1] == Hand.None ? Hand.Rock : _choices[1];

            _result = new RpsResult
            {
                P1 = _players[0],
                P2 = _players[1],
                H1 = hand0,
                H2 = hand1,
                P1Outcome = (byte)DetermineOutcome(hand0, hand1),
                P2Outcome = (byte)DetermineOutcome(hand1, hand0)
            };

            _resultReady = true;
            _roundActive = false;

            RoundResolved?.Invoke(_result);
        }

        private static Hand ValidateHand(Hand hand)
        {
            return hand is Hand.Rock or Hand.Paper or Hand.Scissors ? hand : Hand.Rock;
        }

        /// <summary>
        /// Computes the outcome for the first hand against the opponent hand.
        /// </summary>
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
