using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Collects player choices and confirmations for a round. Used by both local and server coordinators.
    /// </summary>
    public sealed class RpsRoundCollectionSink : IDisposable
    {
        private readonly RpsGameEventChannel _channel;
        private readonly TaskCompletionSource<bool> _channelReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<Dictionary<ulong, Hand>> _choicesTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HashSet<ulong> _expectedChoiceIds = new();
        private readonly Dictionary<ulong, Hand> _choicesBuffer = new();

        private TaskCompletionSource<Dictionary<ulong, bool>> _confirmationsTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HashSet<ulong> _expectedConfirmationIds = new();
        private readonly Dictionary<ulong, bool> _confirmationsBuffer = new();

        public RpsRoundCollectionSink(RpsGameEventChannel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));

            if (channel.IsChannelReady)
            {
                _channelReadyTcs.TrySetResult(true);
            }
            else
            {
                _channel.ChannelReady += OnChannelReady;
            }

            _channel.ChoiceSelected += OnChoiceSelected;
            _channel.RoundResultConfirmed += OnRoundResultConfirmed;
        }

        public void Dispose()
        {
            _channel.ChannelReady -= OnChannelReady;
            _channel.ChoiceSelected -= OnChoiceSelected;
            _channel.RoundResultConfirmed -= OnRoundResultConfirmed;
        }

        public Task WaitForChannelReadyAsync(CancellationToken token = default)
            => WaitWithCancellationAsync(_channelReadyTcs.Task, token);

        public Task<Dictionary<ulong, Hand>> WaitForChoicesAsync(IEnumerable<ulong> expectedPlayerIds, CancellationToken token = default)
        {
            if (expectedPlayerIds == null) throw new ArgumentNullException(nameof(expectedPlayerIds));
            _expectedChoiceIds = new HashSet<ulong>(expectedPlayerIds);
            _choicesTcs = new TaskCompletionSource<Dictionary<ulong, Hand>>(TaskCreationOptions.RunContinuationsAsynchronously);
            TryCompleteChoices();
            return WaitWithCancellationAsync(_choicesTcs.Task, token);
        }

        public Task<Dictionary<ulong, bool>> WaitForConfirmationsAsync(IEnumerable<ulong> expectedPlayerIds, CancellationToken token = default)
        {
            if (expectedPlayerIds == null) throw new ArgumentNullException(nameof(expectedPlayerIds));
            _expectedConfirmationIds = new HashSet<ulong>(expectedPlayerIds);
            _confirmationsTcs = new TaskCompletionSource<Dictionary<ulong, bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
            TryCompleteConfirmations();
            return WaitWithCancellationAsync(_confirmationsTcs.Task, token);
        }

        /// <summary>
        /// Call once per round to reset per-round buffers.
        /// </summary>
        public void ResetForNewRound()
        {
            _choicesTcs = new TaskCompletionSource<Dictionary<ulong, Hand>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _confirmationsTcs = new TaskCompletionSource<Dictionary<ulong, bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _expectedChoiceIds.Clear();
            _expectedConfirmationIds.Clear();
            _choicesBuffer.Clear();
            _confirmationsBuffer.Clear();
        }

        private void OnChannelReady()
        {
            _channelReadyTcs.TrySetResult(true);
        }

        private void OnChoiceSelected(ulong playerId, Hand hand)
        {
            if (_expectedChoiceIds.Count == 0 || !_expectedChoiceIds.Contains(playerId))
            {
                return;
            }

            _choicesBuffer[playerId] = hand;
            TryCompleteChoices();
        }

        private void OnRoundResultConfirmed(ulong playerId, bool continueGame)
        {
            if (_expectedConfirmationIds.Count == 0 || !_expectedConfirmationIds.Contains(playerId))
            {
                return;
            }

            _confirmationsBuffer[playerId] = continueGame;
            TryCompleteConfirmations();
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
    }
}
