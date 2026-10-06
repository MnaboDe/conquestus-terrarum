using UnityEngine;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class UiFlowController : MonoBehaviour
    {
        [SerializeField] private MainMenuScreen mainMenu;
        [SerializeField] private ConnectingScreen connecting;
        [SerializeField] private WorldHudScreen world;
        [SerializeField] private ErrorPanel errorOverlay;
        [SerializeField] private LoadingOverlay loadingOverlay;
        [SerializeField] private ConfirmationDialog forgetConfirmation;
        private readonly UiFlowStateMachine _flow = new UiFlowStateMachine();
        private MatchController _controller;
        private bool _subscribed;
        public ClientUiState State => _flow.State;

        public void Initialize(MatchController controller, MapPresenter map)
        {
            _controller = controller;
            world.Initialize(map);
            Subscribe();
            Refresh();
        }

        private void OnEnable() { if (_controller != null) { Subscribe(); Refresh(); } }
        private void OnDisable() { Unsubscribe(); if (forgetConfirmation != null) forgetConfirmation.Hide(); }
        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            _controller.PresentationChanged += Refresh;
            mainMenu.CreateRequested += Create;
            mainMenu.JoinRequested += Join;
            mainMenu.ContinueRequested += Continue;
            mainMenu.RetryRequested += Retry;
            mainMenu.ForgetRequested += Forget;
            world.MenuRequested += Menu;
            world.ArmyPanel.RetreatRequested += Retreat;
            errorOverlay.DismissRequested += Dismiss;
            forgetConfirmation.Confirmed += ConfirmForget;
            forgetConfirmation.Cancelled += CancelForget;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            if (_controller != null) _controller.PresentationChanged -= Refresh;
            mainMenu.CreateRequested -= Create;
            mainMenu.JoinRequested -= Join;
            mainMenu.ContinueRequested -= Continue;
            mainMenu.RetryRequested -= Retry;
            mainMenu.ForgetRequested -= Forget;
            world.MenuRequested -= Menu;
            world.ArmyPanel.RetreatRequested -= Retreat;
            errorOverlay.DismissRequested -= Dismiss;
            forgetConfirmation.Confirmed -= ConfirmForget;
            forgetConfirmation.Cancelled -= CancelForget;
        }

        private void Create() { _controller.CreateGame(); Refresh(); }
        private void Join(string code) { _controller.JoinGame(code); Refresh(); }
        private void Continue() { _controller.ContinueGame(); Refresh(); }
        private void Retry() { _controller.Refresh(); Refresh(); }
        private void Forget() { mainMenu.SetInteractionEnabled(false); forgetConfirmation.Show(); }
        private void CancelForget() { mainMenu.SetInteractionEnabled(true); mainMenu.FocusForget(); }
        private void ConfirmForget() { _controller.ForgetSavedGame(); Refresh(); }
        private void Menu() { _controller.Disconnect(); Refresh(); }
        private void Retreat() { _controller.RetreatSelectedArmy(); Refresh(); }
        private void Dismiss() { _controller.DismissError(); Refresh(); }

        private void Refresh()
        {
            _flow.Synchronize(_controller.PresentationRevision, _controller.IsConnected, _controller.IsBusy);
            mainMenu.SetVisible(State == ClientUiState.MainMenu);
            connecting.SetVisible(State == ClientUiState.Connecting);
            world.SetVisible(State == ClientUiState.World);
            if (State == ClientUiState.MainMenu)
                mainMenu.Refresh(_controller.HasSavedSession, _controller.Session != null, _controller.IsBusy, _controller.LastError);
            else if (State == ClientUiState.Connecting) connecting.Refresh(_controller.Status);
            else if (State == ClientUiState.World) world.Refresh(_controller.State, _controller.SelectedArmy, _controller.IsLocalArmy(_controller.SelectedArmy),
                _controller.CanMoveSelected, _controller.CanRetreatSelected, _controller.Status, _controller.IsBusy, _controller.Session?.JoinCode, _controller.LocalPlayerId);
            errorOverlay.Refresh(State == ClientUiState.World ? _controller.LastError : null);
            loadingOverlay.Refresh(State == ClientUiState.World && _controller.IsBusy, _controller.Status);
            if (State != ClientUiState.MainMenu || (!_controller.HasSavedSession && _controller.Session == null)) forgetConfirmation.Hide();
            mainMenu.SetInteractionEnabled(!forgetConfirmation.gameObject.activeSelf);
        }
    }
}
