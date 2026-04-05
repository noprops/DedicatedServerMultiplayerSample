using System;
using System.Collections.Generic;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    internal sealed class MatchAllocationResult
    {
        public bool Success { get; }
        public IReadOnlyList<string> ExpectedAuthIds { get; }
        public int TeamCount { get; }

        public MatchAllocationResult(bool success, IReadOnlyList<string> expectedAuthIds, int teamCount)
        {
            Success = success;
            ExpectedAuthIds = expectedAuthIds ?? Array.Empty<string>();
            TeamCount = Math.Max(1, teamCount);
        }

        public static MatchAllocationResult Failed()
        {
            return new MatchAllocationResult(false, Array.Empty<string>(), 0);
        }
    }
}
