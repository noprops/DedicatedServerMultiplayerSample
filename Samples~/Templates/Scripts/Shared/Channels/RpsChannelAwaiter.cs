using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Shared awaitable/event buffering helper used by RpsGameEventChannel.
    /// </summary>
    internal sealed class RpsChannelAwaiter
    {
        private readonly TaskCompletionSource<bool> _channelReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<(string myName, string opponentName)> _playersReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> _roundResultTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<string> _gameAbortedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<Dictionary<ulong, Hand>> _choicesTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HashSet<ulong> _expectedChoiceIds = new();
        private readonly Dictionary<ulong, Hand> _choicesBuffer = new();

        private TaskCompletionSource<Dictionary<ulong, bool>> _confirmationsTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HashSet<ulong> _expectedConfirmationIds = new();
        private readonly Dictionary<ulong, bool> _confirmationsBuffer = new();

        #region Record notifications (drive awaiters)

        public void RecordChannelReady() => _channelReadyTcs.TrySetResult(true);

        public void RecordPlayersReady(string myName, string opponentName) =>
            _playersReadyTcs.TrySetResult((myName, opponentName));

        public void RecordRoundResult(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue) =>
            _roundResultTcs.TrySetResult((outcome, myHand, opponentHand, canContinue));

        public void RecordGameAborted(string message) =>
            _gameAbortedTcs.TrySetResult(message);

        public void RecordChoice(ulong playerId, Hand hand)
        {
            if (_expectedChoiceIds.Count == 0 || !_expectedChoiceIds.Contains(playerId))
            {
                return;
            }

            _choicesBuffer[playerId] = hand;
            TryCompleteChoices();
        }

        public void RecordConfirmation(ulong playerId, bool continueGame)
        {
            if (_expectedConfirmationIds.Count == 0 || !_expectedConfirmationIds.Contains(playerId))
            {
                return;
            }

            _confirmationsBuffer[playerId] = continueGame;
            TryCompleteConfirmations();
        }

        #endregion

        #region Await APIs

        public Task WaitForChannelReadyAsync(CancellationToken token)
            => WaitWithCancellationAsync(_channelReadyTcs.Task, token);

        public Task<(string myName, string opponentName)> WaitForPlayersReadyAsync(CancellationToken token)
            => WaitWithCancellationAsync(_playersReadyTcs.Task, token);

        public Task<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> WaitForRoundResultAsync(CancellationToken token)
            => WaitWithCancellationAsync(_roundResultTcs.Task, token);

        public Task<string> WaitForGameAbortedAsync(CancellationToken token)
            => WaitWithCancellationAsync(_gameAbortedTcs.Task, token);

        public Task<Dictionary<ulong, Hand>> WaitForChoicesAsync(HashSet<ulong> expectedIds, CancellationToken token)
        {
            _expectedChoiceIds = new HashSet<ulong>(expectedIds);
            _choicesTcs = new TaskCompletionSource<Dictionary<ulong, Hand>>(TaskCreationOptions.RunContinuationsAsynchronously);
            TryCompleteChoices();
            return WaitWithCancellationAsync(_choicesTcs.Task, token);
        }

        public Task<Dictionary<ulong, bool>> WaitForConfirmationsAsync(HashSet<ulong> expectedIds, CancellationToken token)
        {
            _expectedConfirmationIds = new HashSet<ulong>(expectedIds);
            _confirmationsTcs = new TaskCompletionSource<Dictionary<ulong, bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
            TryCompleteConfirmations();
            return WaitWithCancellationAsync(_confirmationsTcs.Task, token);
        }

        #endregion

        #region Reset helpers

        public void ResetResultAwaiter()
        {
            _roundResultTcs = new TaskCompletionSource<(RoundOutcome, Hand, Hand, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ResetRoundAwaiters()
        {
            _choicesTcs = new TaskCompletionSource<Dictionary<ulong, Hand>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmationsTcs = new TaskCompletionSource<Dictionary<ulong, bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _expectedChoiceIds.Clear();
            _expectedConfirmationIds.Clear();
            _choicesBuffer.Clear();
            _confirmationsBuffer.Clear();
        }

        public void ResetAllAwaiters()
        {
            ResetRoundAwaiters();
            ResetResultAwaiter();
        }

        #endregion

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

        private void TryCompleteConfirmations()
        {
            if (_confirmationsTcs.Task.IsCompleted || _expectedConfirmationIds.Count == 0)
            {
                return;
            }

            if (_expectedConfirmationIds.All(id => _confirmationsBuffer.ContainsKey(id)))
            {
                _confirmationsTcs.TrySetResult(new Dictionary<ulong, bool>(_confirmationsBuffer));
            }
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

        private static async Task WaitWithCancellationAsync(Task task, CancellationToken token)
        {
            if (!token.CanBeCanceled)
            {
                await task;
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => tcs.TrySetCanceled(token));

            var completed = await Task.WhenAny(task, tcs.Task);
            await completed;
        }
    }
}
