namespace DedicatedServerMultiplayerSample.Server.Core
{
    public enum ShutdownKind
    {
        Normal,
        Error,
        StartTimeout,
        AllPlayersDisconnected
    }
}
