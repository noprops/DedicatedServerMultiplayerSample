using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public abstract partial class RpsGameEventChannel
    {
        public Task WaitForChannelReadyAsync(CancellationToken token = default)
            => Awaiter.WaitForChannelReadyAsync(token);

        public Task<(string myName, string opponentName)> WaitForPlayersReadyAsync(CancellationToken token = default)
            => Awaiter.WaitForPlayersReadyAsync(token);

        public Task<bool> WaitForRoundStartDecisionAsync(CancellationToken token = default)
            => Awaiter.WaitForRoundStartDecisionAsync(token);

        public Task<(RoundOutcome outcome, Hand myHand, Hand opponentHand, bool canContinue)> WaitForRoundResultAsync(CancellationToken token = default)
            => Awaiter.WaitForRoundResultAsync(token);

        public Task<string> WaitForGameAbortedAsync(CancellationToken token = default)
            => Awaiter.WaitForGameAbortedAsync(token);

        public Task<Dictionary<ulong, Hand>> WaitForChoicesAsync(HashSet<ulong> expectedIds, TimeSpan timeout, CancellationToken token = default)
            => Awaiter.WaitForChoicesAsync(expectedIds, timeout, token);

        public Task<bool> WaitForConfirmationsAsync(HashSet<ulong> expectedIds, TimeSpan timeout, CancellationToken token = default)
            => Awaiter.WaitForConfirmationsAsync(expectedIds, timeout, token);

        private RpsGameEventChannelAwaiter Awaiter => _awaiter ??= new RpsGameEventChannelAwaiter(this);
        private RpsGameEventChannelAwaiter _awaiter;
    }
}
