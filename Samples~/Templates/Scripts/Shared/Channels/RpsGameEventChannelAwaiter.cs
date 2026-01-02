using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    internal sealed class RpsGameEventChannelAwaiter
    {
        private readonly RpsGameEventChannel _channel;

        // ==== Channel lifecycle ====

        // Channel ready (shared by server/client).
        private readonly TaskCompletionSource<bool> _channelReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // ==== Coordinator -> UI ====

        // Players ready.
        private readonly TaskCompletionSource<(string myName, string opponentName)> _playersReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Round started.
        private TaskCompletionSource<bool> _roundStartDecisionTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Round result.
        private TaskCompletionSource<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> _roundResultTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Game aborted.
        private readonly TaskCompletionSource<string> _gameAbortedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // ==== UI -> Coordinator ====

        // Choices aggregation.
        private TaskCompletionSource<Dictionary<ulong, Hand>> _choicesTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HashSet<ulong> _expectedChoiceIds = new();
        private readonly Dictionary<ulong, Hand> _choicesBuffer = new();

        // Confirmations aggregation.
        private TaskCompletionSource<bool> _confirmationsTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HashSet<ulong> _expectedConfirmationIds = new();
        private readonly Dictionary<ulong, bool> _confirmationsBuffer = new();

        public RpsGameEventChannelAwaiter(RpsGameEventChannel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            HookEvents();
            ResetRoundOutputAwaiters();
            ResetRoundInputAwaiters();

            if (_channel.IsChannelReady)
            {
                RecordChannelReady();
            }
        }

        // ==== Channel lifecycle ====

        public Task WaitForChannelReadyAsync(CancellationToken token)
            => WaitWithCancellationAsync(_channelReadyTcs.Task, token);

        private void RecordChannelReady() => _channelReadyTcs.TrySetResult(true);

        // ==== Coordinator -> UI ====

        public Task<(string myName, string opponentName)> WaitForPlayersReadyAsync(CancellationToken token)
            => WaitWithCancellationAsync(_playersReadyTcs.Task, token);

        private void RecordPlayersReady(string myName, string opponentName)
            => _playersReadyTcs.TrySetResult((myName, opponentName));

        // ==== Round start decision ====

        public Task<bool> WaitForRoundStartDecisionAsync(CancellationToken token)
            => WaitWithCancellationAsync(_roundStartDecisionTcs.Task, token);

        private void RecordRoundStartDecision(bool startRound)
        {
            _roundStartDecisionTcs.TrySetResult(startRound);
            ResetRoundOutputAwaiters();
        }

        // ==== Round result ====

        public Task<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> WaitForRoundResultAsync(CancellationToken token)
            => WaitWithCancellationAsync(_roundResultTcs.Task, token);

        private void RecordRoundResult(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)
            => _roundResultTcs.TrySetResult((outcome, myHand, opponentHand, canContinue));

        // ==== Continue decision ====

        // ==== Game aborted ====

        public Task<string> WaitForGameAbortedAsync(CancellationToken token)
            => WaitWithCancellationAsync(_gameAbortedTcs.Task, token);

        private void RecordGameAborted(string message)
            => _gameAbortedTcs.TrySetResult(message);

        // ==== Round state reset ====

        private void ResetRoundOutputAwaiters()
        {
            _roundStartDecisionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _roundResultTcs = new TaskCompletionSource<(RoundOutcome, Hand, Hand, bool)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void ResetRoundInputAwaiters()
        {
            _choicesTcs = new TaskCompletionSource<Dictionary<ulong, Hand>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmationsTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _expectedChoiceIds.Clear();
            _expectedConfirmationIds.Clear();
            _choicesBuffer.Clear();
            _confirmationsBuffer.Clear();
        }

        // ==== UI -> Coordinator ====

        // ==== Choices aggregation ====

        public async Task<Dictionary<ulong, Hand>> WaitForChoicesAsync(HashSet<ulong> expectedIds, TimeSpan timeout, CancellationToken token)
        {
            ResetChoicesAggregation(expectedIds);
            TryCompleteChoices();
            return await WaitForChoicesWithTimeoutAsync(timeout, token);
        }

        private void RecordChoice(ulong playerId, Hand hand)
        {
            if (_expectedChoiceIds.Count == 0 || !_expectedChoiceIds.Contains(playerId))
            {
                return;
            }

            _choicesBuffer[playerId] = hand;
            TryCompleteChoices();
        }

        private void TryCompleteChoices()
        {
            if (_choicesTcs.Task.IsCompleted || _expectedChoiceIds.Count == 0)
            {
                return;
            }

            if (_expectedChoiceIds.All(id => _choicesBuffer.ContainsKey(id)))
            {
                _choicesTcs.TrySetResult(new Dictionary<ulong, Hand>(_choicesBuffer));
            }
        }

        private async Task<Dictionary<ulong, Hand>> WaitForChoicesWithTimeoutAsync(TimeSpan timeout, CancellationToken token)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                return await WaitWithCancellationAsync(_choicesTcs.Task, token);
            }

            if (timeout <= TimeSpan.Zero)
            {
                return new Dictionary<ulong, Hand>(_choicesBuffer);
            }

            var timeoutTask = Task.Delay(timeout);
            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => cancelTcs.TrySetCanceled(token));

            var completed = await Task.WhenAny(_choicesTcs.Task, timeoutTask, cancelTcs.Task);
            if (completed == _choicesTcs.Task)
            {
                return await _choicesTcs.Task;
            }

            if (completed == cancelTcs.Task)
            {
                await cancelTcs.Task;
            }

            return new Dictionary<ulong, Hand>(_choicesBuffer);
        }

        // ==== Confirmations aggregation ====

        public async Task<bool> WaitForConfirmationsAsync(HashSet<ulong> expectedIds, TimeSpan timeout, CancellationToken token)
        {
            ResetConfirmationsAggregation(expectedIds);
            TryCompleteConfirmations();
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                return await WaitWithCancellationAsync(_confirmationsTcs.Task, token);
            }

            if (timeout <= TimeSpan.Zero)
            {
                return false;
            }

            var timeoutTask = Task.Delay(timeout);
            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => cancelTcs.TrySetCanceled(token));

            var completed = await Task.WhenAny(_confirmationsTcs.Task, timeoutTask, cancelTcs.Task);
            if (completed == _confirmationsTcs.Task)
            {
                return await _confirmationsTcs.Task;
            }

            if (completed == cancelTcs.Task)
            {
                await cancelTcs.Task;
            }

            return false;
        }

        private void ResetChoicesAggregation(HashSet<ulong> expectedIds)
        {
            _expectedChoiceIds = new HashSet<ulong>(expectedIds);
            _choicesTcs = new TaskCompletionSource<Dictionary<ulong, Hand>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _choicesBuffer.Clear();
        }

        private void ResetConfirmationsAggregation(HashSet<ulong> expectedIds)
        {
            _expectedConfirmationIds = new HashSet<ulong>(expectedIds);
            _confirmationsTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmationsBuffer.Clear();
        }

        private void RecordConfirmation(ulong playerId, bool continueGame)
        {
            if (_expectedConfirmationIds.Count == 0 || !_expectedConfirmationIds.Contains(playerId))
            {
                return;
            }

            _confirmationsBuffer[playerId] = continueGame;
            if (!continueGame)
            {
                _confirmationsTcs.TrySetResult(false);
                return;
            }

            TryCompleteConfirmations();
        }

        private void TryCompleteConfirmations()
        {
            if (_confirmationsTcs.Task.IsCompleted || _expectedConfirmationIds.Count == 0)
            {
                return;
            }

            if (_expectedConfirmationIds.All(id => _confirmationsBuffer.ContainsKey(id)))
            {
                _confirmationsTcs.TrySetResult(true);
            }
        }

        // ==== Event hooks ====

        private void HookEvents()
        {
            _channel.ChannelReady += RecordChannelReady;
            _channel.PlayersReady += RecordPlayersReady;
            _channel.RoundStartDecision += RecordRoundStartDecision;
            _channel.RoundResultReady += RecordRoundResult;
            _channel.GameAborted += RecordGameAborted;
            _channel.ChoiceSelected += RecordChoice;
            _channel.RoundResultConfirmed += RecordConfirmation;
        }

        private static async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken token)
        {
            if (!token.CanBeCanceled)
            {
                return await task;
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => tcs.TrySetCanceled(token));

            var completed = await Task.WhenAny(task, tcs.Task);
            return await completed;
        }
    }
}
