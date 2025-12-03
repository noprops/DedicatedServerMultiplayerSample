using System;
using DedicatedServerMultiplayerSample.Client;
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMultiplayerSample.Samples.Client.UI.Menu
{
    /// <summary>
    /// Friend match UI controller: switches landing/create/join views and delegates flows to CreateRoomUI/JoinRoomUI.
    /// </summary>
    public sealed class FriendMatchUI : MonoBehaviour
    {
        [SerializeField] private ViewModal modal;

        [Header("Views")]
        [SerializeField] private GameObject landingView;
        [SerializeField] private GameObject createView;
        [SerializeField] private GameObject joinView;

        [Header("Landing View")]
        [SerializeField] private Button landingCreateButton;
        [SerializeField] private Button landingJoinButton;
        [SerializeField] private Button landingCloseButton;

        [Header("Create View")]
        [SerializeField] private CreateRoomUI createUi;

        [Header("Join View")]
        [SerializeField] private JoinRoomUI joinUi;

        public event Action CloseButtonPressed;

        private FriendMatchService _service;
        private void Awake()
        {
            landingCreateButton.onClick.AddListener(ShowCreateView);
            landingJoinButton.onClick.AddListener(ShowJoinView);
            landingCloseButton.onClick.AddListener(HandleLandingCloseClicked);

            createUi.OnCloseRequested += HandleCloseButtonClickedAsync;
            joinUi.OnCloseRequested += HandleCloseButtonClickedAsync;
        }

        private void Start()
        {
            _service = new FriendMatchService(ClientSingleton.Instance?.Matchmaker, ClientData.Instance);
            createUi.SetService(_service);
            joinUi.SetService(_service);
        }

        private void OnDestroy()
        {
            landingCreateButton.onClick.RemoveListener(ShowCreateView);
            landingJoinButton.onClick.RemoveListener(ShowJoinView);
            landingCloseButton.onClick.RemoveListener(HandleLandingCloseClicked);
            createUi.OnCloseRequested -= HandleCloseButtonClickedAsync;
            joinUi.OnCloseRequested -= HandleCloseButtonClickedAsync;
        }

        public void Show()
        {
            landingCloseButton.interactable = true;
            modal.Show(landingView);
        }

        private void HandleLandingCloseClicked()
        {
            HandleCloseButtonClickedAsync(landingCloseButton);
        }

        private async void HandleCloseButtonClickedAsync(Button pressedButton)
        {
            pressedButton.interactable = false;
            await _service.CancelMatchmakingAsync();
            modal.Hide();
            CloseButtonPressed?.Invoke();
        }

        private void ShowCreateView()
        {
            createUi.OnShow();
            modal.ShowView(createView);
        }

        private void ShowJoinView()
        {
            joinUi.OnShow();
            modal.ShowView(joinView);
        }
    }
}
