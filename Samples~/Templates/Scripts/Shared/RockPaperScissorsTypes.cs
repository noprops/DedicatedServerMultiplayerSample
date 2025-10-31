using System;
namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public enum GamePhase : byte
    {
        None = 0,
        WaitingForPlayers = 1,
        Choosing = 2,
        Resolving = 3,
        Finished = 4,
        StartFailed = 5
    }

    public enum Hand : byte
    {
        None = 0,
        Rock = 1,
        Paper = 2,
        Scissors = 3
    }

    public static class HandExtensions
    {
        private static readonly Random _random = new Random();

        /// <summary>
        /// Returns a random non-empty hand (Rock/Paper/Scissors).
        /// </summary>
        public static Hand RandomHand()
        {
            var value = _random.Next(1, 4);
            return (Hand)value;
        }

        /// <summary>
        /// Provides a human-readable label for the supplied hand.
        /// </summary>
        public static string ToString(this Hand hand) => hand switch
        {
            Hand.Rock => "Rock",
            Hand.Paper => "Paper",
            Hand.Scissors => "Scissors",
            _ => "-"
        };
    }

    public enum RoundOutcome : byte
    {
        Lose = 0,
        Draw = 1,
        Win = 2
    }

    public static class RoundOutcomeExtensions
    {
        public static string ToString(RoundOutcome outcome) => outcome switch
        {
            RoundOutcome.Win => "Win",
            RoundOutcome.Draw => "Draw",
            RoundOutcome.Lose => "Lose",
            _ => "-"
        };
    }

    public struct RpsResult
    {
        public ulong Player1Id;
        public ulong Player2Id;
        public Hand Player1Hand;
        public Hand Player2Hand;
        public RoundOutcome Player1Outcome;
        public RoundOutcome Player2Outcome;
    }

}
