using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Pure rock-paper-scissors rules engine that orchestrates round flow independent of transport or UI.
    /// </summary>
    public sealed class RockPaperScissorsGameLogic : IDisposable
    {
        private readonly ulong[] _players = new ulong[2];
        private readonly Hand[] _choices = new Hand[2];
        private readonly bool[] _hasChoice = new bool[2];

        private bool _roundActive;
        private bool _resultReady;
        private Func<Hand> _handProvider;
        private TaskCompletionSource<RpsResult> _roundCompletion;
        private CancellationTokenSource _timeoutSource;
        private CancellationTokenRegistration _timeoutRegistration;
        private CancellationTokenRegistration _cancellationRegistration;
        private RpsResult _result;

        /// <summary>
        /// Begins a new round, resetting state and wiring timeout/cancellation handlers.
        /// </summary>
        public void StartRound(
            ulong player0,
            ulong player1,
            TimeSpan timeout,
            Func<Hand> handProvider,
            CancellationToken cancellationToken = default)
        {
            if (handProvider == null)
            {
                throw new ArgumentNullException(nameof(handProvider));
            }

            CleanupRegistrations();

            _players[0] = player0;
            _players[1] = player1;

            _choices[0] = Hand.None;
            _choices[1] = Hand.None;
            _hasChoice[0] = false;
            _hasChoice[1] = false;

            _resultReady = false;
            _roundActive = true;
            _handProvider = handProvider;
            _roundCompletion = new TaskCompletionSource<RpsResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            _timeoutSource = new CancellationTokenSource();
            _timeoutRegistration = _timeoutSource.Token.Register(OnTimeout);
            _timeoutSource.CancelAfter(timeout);

            if (cancellationToken.CanBeCanceled)
            {
                _cancellationRegistration = cancellationToken.Register(OnCanceled);
            }
        }

        /// <summary>
        /// Task that completes when the round resolves, either by player submissions or timeout.
        /// </summary>
        public Task<RpsResult> RoundTask =>
            _roundCompletion?.Task ?? Task.FromException<RpsResult>(new InvalidOperationException("Round not started."));

        /// <summary>
        /// Attempts to record a hand for the given slot (0 or 1). Returns false when the slot already chose or the hand is invalid.
        /// </summary>
        public bool Submit(int slot, Hand hand)
        {
            if (!_roundActive || slot < 0 || slot > 1 || hand is Hand.None || _hasChoice[slot])
            {
                return false;
            }

            _choices[slot] = ValidateHand(hand);
            _hasChoice[slot] = true;

            if (_hasChoice[0] && _hasChoice[1])
            {
                ResolveRound();
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
                ResolveRound();
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

        private void ResolveRound()
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

            CompleteRound(_result);
        }

        private void OnTimeout()
        {
            if (!_roundActive)
            {
                return;
            }

            try
            {
                AssignIfMissing(_handProvider ?? (() => Hand.Rock));
            }
            catch (Exception)
            {
                // Fallback: if the provider throws, ensure the round still resolves with defaults.
                AssignIfMissing(() => Hand.Rock);
            }

            if (!_resultReady)
            {
                ResolveRound();
            }
        }

        private void OnCanceled()
        {
            if (_roundActive)
            {
                _roundActive = false;
                DisposeTimeout();
                _handProvider = null;
                _roundCompletion?.TrySetCanceled();
            }
        }

        private void CompleteRound(RpsResult result)
        {
            if (_resultReady)
            {
                return;
            }

            _result = result;
            _resultReady = true;
            _roundActive = false;

            DisposeTimeout();
            _handProvider = null;

            _roundCompletion?.TrySetResult(result);
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

        private void DisposeTimeout()
        {
            _timeoutRegistration.Dispose();
            _timeoutRegistration = default;

            _timeoutSource?.Dispose();
            _timeoutSource = null;
        }

        private void CleanupRegistrations()
        {
            DisposeTimeout();
            _cancellationRegistration.Dispose();
            _cancellationRegistration = default;
            _roundActive = false;
            _roundCompletion = null;
        }

        public void Dispose()
        {
            CleanupRegistrations();
        }
    }
}
