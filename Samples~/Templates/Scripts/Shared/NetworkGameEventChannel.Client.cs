#if !UNITY_SERVER && !ENABLE_UCS_SERVER
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public sealed partial class NetworkGameEventChannel
    {
        partial void HandleClientChoiceSelected(Hand choice)
        {
            rpcProxy.SubmitChoice(choice);
        }

        partial void HandleClientRoundResultConfirmed()
        {
            rpcProxy.ConfirmRoundResult();
            ClientSingleton.Instance?.GameManager?.Disconnect();
        }

        partial void HandleClientAbortConfirmed()
        {
            ClientSingleton.Instance?.GameManager?.Disconnect();
        }
    }
}
#endif
