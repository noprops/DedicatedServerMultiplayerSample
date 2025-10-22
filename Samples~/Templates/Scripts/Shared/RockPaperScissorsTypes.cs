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

    public enum Hand
    {
        None = 0,
        Rock = 1,
        Paper = 2,
        Scissors = 3
    }

    public enum RoundOutcome : byte
    {
        Lose = 0,
        Draw = 1,
        Win = 2
    }

    public struct RpsResult : INetworkSerializable
    {
        public ulong P1;
        public ulong P2;
        public Hand H1;
        public Hand H2;
        public byte P1Outcome;
        public byte P2Outcome;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref P1);
            serializer.SerializeValue(ref P2);
            serializer.SerializeValue(ref H1);
            serializer.SerializeValue(ref H2);
            serializer.SerializeValue(ref P1Outcome);
            serializer.SerializeValue(ref P2Outcome);
        }
    }

}
