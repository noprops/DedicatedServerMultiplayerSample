using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// Awaitable wrapper for UI-facing notifications (channel ready, players ready, round result, abort).
    /// Works for both local and network channels.
    /// </summary>
    public sealed class RpsUiEventSink : IDisposable
    {
        private readonly RpsGameEventChannel _channel;

        private readonly TaskCompletionSource<bool> _channelReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<(string myName, string opponentName)> _playersReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> _roundResultTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _gameAbortedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RpsUiEventSink(RpsGameEventChannel channel)
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

            _channel.PlayersReady += OnPlayersReady;
            _channel.RoundResultReady += OnRoundResultReady;
            _channel.GameAborted += OnGameAborted;
        }

        public void Dispose()
        {
            _channel.ChannelReady -= OnChannelReady;
            _channel.PlayersReady -= OnPlayersReady;
            _channel.RoundResultReady -= OnRoundResultReady;
            _channel.GameAborted -= OnGameAborted;
        }

        public Task WaitForChannelReadyAsync(CancellationToken token = default)
            => WaitWithCancellationAsync(_channelReadyTcs.Task, token);

        public Task<(string myName, string opponentName)> WaitForPlayersReadyAsync(CancellationToken token = default)
            => WaitWithCancellationAsync(_playersReadyTcs.Task, token);

        public Task<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> WaitForRoundResultAsync(CancellationToken token = default)
            => WaitWithCancellationAsync(_roundResultTcs.Task, token);

        public Task<string> WaitForGameAbortedAsync(CancellationToken token = default)
            => WaitWithCancellationAsync(_gameAbortedTcs.Task, token);

        /// <summary>
        /// Call once per round to reset per-round UI notifications.
        /// </summary>
        public void ResetForNewRound()
        {
            _roundResultTcs = new TaskCompletionSource<(RoundOutcome, Hand, Hand, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void OnChannelReady()
        {
            _channelReadyTcs.TrySetResult(true);
        }

        private void OnPlayersReady(string myName, string opponentName)
        {
            _playersReadyTcs.TrySetResult((myName, opponentName));
        }

        private void OnRoundResultReady(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)
        {
            _roundResultTcs.TrySetResult((outcome, myHand, opponentHand, canContinue));
        }

        private void OnGameAborted(string reason)
        {
            _gameAbortedTcs.TrySetResult(reason);
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
