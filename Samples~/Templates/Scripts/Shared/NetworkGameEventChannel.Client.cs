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
            ClientSingleton.Instance?.DisconnectFromServer();
        }

        partial void HandleClientAbortConfirmed()
        {
            ClientSingleton.Instance?.DisconnectFromServer();
        }
    }
}
#endif
