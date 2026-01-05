using System;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public abstract partial class RpsGameEventChannel
    {
        private TaskCompletionSource<bool> _channelReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<(string myName, string opponentName)> _playersReadyTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _roundStartedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> _roundResultTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource<bool> _continueDecisionTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WaitForChannelReadyAsync(CancellationToken token)
        {
            await WaitWithCancellationAsync(_channelReadyTcs.Task, token);
        }

        // ==== Awaitable UI waits ====

        public async Task<(string myName, string opponentName)> WaitForPlayersReadyAsync(CancellationToken token)
        {
            var result = await WaitWithCancellationAsync(_playersReadyTcs.Task, token);
            _playersReadyTcs = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
            return result;
        }

        public async Task WaitForRoundStartedAsync(CancellationToken token)
        {
            await WaitWithCancellationAsync(_roundStartedTcs.Task, token);
            _roundStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> WaitForRoundResultAsync(
            CancellationToken token)
        {
            var result = await WaitWithCancellationAsync(_roundResultTcs.Task, token);
            _roundResultTcs = new TaskCompletionSource<(RoundOutcome, Hand, Hand, bool)>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return result;
        }

        public async Task<bool> WaitForContinueDecisionAsync(CancellationToken token)
        {
            var result = await WaitWithCancellationAsync(_continueDecisionTcs.Task, token);
            _continueDecisionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return result;
        }

        private async Task WaitWithCancellationAsync(Task task, CancellationToken token)
        {
            if (!token.CanBeCanceled)
            {
                await task;
                return;
            }

            if (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }

            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelTcs.TrySetResult(true)))
            {
                var completed = await Task.WhenAny(task, cancelTcs.Task);
                if (completed != task)
                {
                    throw new OperationCanceledException(token);
                }

                await task;
            }
        }

        private async Task<T> WaitWithCancellationAsync<T>(Task<T> task, CancellationToken token)
        {
            if (!token.CanBeCanceled)
            {
                return await task;
            }

            if (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }

            var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelTcs.TrySetResult(true)))
            {
                var completed = await Task.WhenAny(task, cancelTcs.Task);
                if (completed != task)
                {
                    throw new OperationCanceledException(token);
                }

                return await task;
            }
        }
    }
}
