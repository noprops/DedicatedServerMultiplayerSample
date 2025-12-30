#if !UNITY_SERVER && !ENABLE_UCS_SERVER
using DedicatedServerMultiplayerSample.Client;
#endif

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public sealed partial class NetworkGameEventChannel
    {
#if !UNITY_SERVER && !ENABLE_UCS_SERVER

        partial void HandleClientChoiceSelected(Hand choice)
        {
            rpcProxy.SubmitChoice(choice);
        }

        public override void RaiseChoiceSelectedForPlayer(ulong playerId, Hand hand)
        {
            // Network path always derives sender id via transport; relay using the client-side hook.
            rpcProxy.SubmitChoice(hand);
        }

        partial void HandleClientRoundResultConfirmed(bool continueGame)
        {
            // Notify local listeners without relying on a client id (server will include sender id via RPC).
            InvokeRoundResultConfirmed(0, continueGame);
            // Forward the selection to the server via RPC proxy.
            rpcProxy.ConfirmRoundResult(continueGame);
        }

        partial void HandleClientAbortConfirmed()
        {
            // Notify local listeners.
            InvokeGameAbortConfirmed();
        }
#else
        partial void HandleClientChoiceSelected(Hand choice)
        {
        }

        public override void RaiseChoiceSelectedForPlayer(ulong playerId, Hand hand)
        {
        }

        partial void HandleClientRoundResultConfirmed(bool continueGame)
        {
        }

        partial void HandleClientAbortConfirmed()
        {
        }
#endif
    }
}
