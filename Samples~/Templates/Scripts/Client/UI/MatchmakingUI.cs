using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Client;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI
{
    public class MatchmakingUI : MonoBehaviour
    {
        [SerializeField] private StartCancelStatusView controls;
        [SerializeField] private string queueName = "default-queue";

        private ClientMatchmaker matchmaker;
        private MatchmakingWorkflow workflow;
        private CancellationTokenSource lifecycleCts;

        public event Action StartControlsDisplayed;

        private async void Start()
        {
            matchmaker = ClientSingleton.Instance?.Matchmaker;

            if (matchmaker == null || controls == null)
            {
                controls?.SetStatus("Client not initialized");
                return;
            }

            workflow = new MatchmakingWorkflow(matchmaker, controls);
            lifecycleCts = new CancellationTokenSource();

            try
            {
                await workflow.RunAsync(queueName, BuildPayload, lifecycleCts.Token);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
        }

        private void OnEnable()
        {
            if (controls != null)
            {
                controls.StartViewDisplayed += HandleStartViewDisplayed;
                HandleStartViewDisplayed();
            }
        }

        private void OnDisable()
        {
            if (controls != null)
            {
                controls.StartViewDisplayed -= HandleStartViewDisplayed;
            }
        }

        private MatchmakingPayload BuildPayload()
        {
            var source = ClientData.Instance;
            return new MatchmakingPayload(
                source?.GetPlayerProperties(),
                source?.GetTicketAttributes(),
                source?.GetConnectionData(),
                source?.GetSessionProperties());
        }

        private void OnDestroy()
        {
            lifecycleCts?.Cancel();
            lifecycleCts?.Dispose();
        }

        private void HandleStartViewDisplayed()
        {
            StartControlsDisplayed?.Invoke();
        }
    }
}
