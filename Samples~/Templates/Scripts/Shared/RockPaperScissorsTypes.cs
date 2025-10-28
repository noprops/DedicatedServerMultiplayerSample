using System;
using Unity.Collections;
using Unity.Netcode;

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
        public static string ToDisplayString(this Hand hand) => hand switch
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

    public struct RpsResult : INetworkSerializable
    {
        public ulong Player1Id;
        public ulong Player2Id;
        public Hand Player1Hand;
        public Hand Player2Hand;
        public RoundOutcome Player1Outcome;
        public RoundOutcome Player2Outcome;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Player1Id);
            serializer.SerializeValue(ref Player2Id);
            serializer.SerializeValue(ref Player1Hand);
            serializer.SerializeValue(ref Player2Hand);
            serializer.SerializeValue(ref Player1Outcome);
            serializer.SerializeValue(ref Player2Outcome);
        }
    }

}
