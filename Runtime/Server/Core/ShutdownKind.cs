#if UNITY_SERVER || ENABLE_UCS_SERVER
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
#endif
