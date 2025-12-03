using DedicatedServerMultiplayerSample.Samples.Client.UI.Common;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Handles top-level menu button actions (e.g., launching the local CPU scene).
    /// </summary>
    public sealed class MenuController : MonoBehaviour
    {
        // References to the three main menu buttons/flows.
        [SerializeField] private RankedMatchButtonUI rankedMatchButton;
        [SerializeField] private Button friendButton;
        [SerializeField] private Button cpuButton;
        [SerializeField] private ButtonLockGroup buttonLockGroup;
        // Modal views: main menu and friend flow.
        [SerializeField] private ViewModal menuViewModal;
        [SerializeField] private GameObject mainMenuView;
        [SerializeField] private GameObject friendFlowView;
        [SerializeField] private FriendMatchUI friendMatchUI;

        private const string CpuSceneName = "game_cpu";

        private void Awake()
        {
            menuViewModal.ShowView(mainMenuView);
            cpuButton.onClick.AddListener(StartCpuGame);
            friendButton.onClick.AddListener(OpenFriendFlow);

            rankedMatchButton.MatchmakingAborted += HandleOnlineCancelCompleted;
            friendMatchUI.CloseButtonPressed += HandleFriendModalCloseButton;
        }

        private void OnDestroy()
        {
            cpuButton.onClick.RemoveListener(StartCpuGame);
            friendButton.onClick.RemoveListener(OpenFriendFlow);
            rankedMatchButton.MatchmakingAborted -= HandleOnlineCancelCompleted;
            friendMatchUI.CloseButtonPressed -= HandleFriendModalCloseButton;
        }
        // Opens the friend flow view when the Friend button is pressed.
        private void OpenFriendFlow()
        {
            menuViewModal.ShowView(friendFlowView);
            friendMatchUI.Show();
        }
        // Loads the CPU-only scene when the CPU button is pressed.
        public void StartCpuGame()
        {
            SceneManager.LoadScene(CpuSceneName);
        }
        // Called when the online match UI finishes cancelling and returns to the start state.
        private void HandleOnlineCancelCompleted()
        {
            buttonLockGroup.ResetButtons();
        }
        // Resets back to the main menu once the friend flow closes.
        private void HandleFriendModalCloseButton()
        {
            menuViewModal.ShowView(mainMenuView);
            buttonLockGroup.ResetButtons();
        }
    }
}
