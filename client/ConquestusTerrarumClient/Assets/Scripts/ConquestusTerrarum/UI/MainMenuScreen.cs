using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class MainMenuScreen : UiScreen
    {
        [SerializeField] private Button createButton, joinButton, continueButton, forgetButton, retryButton;
        [SerializeField] private GameObject joinForm;
        [SerializeField] private TMP_InputField joinInput;
        [SerializeField] private TMP_Text validationText, savedSessionText;
        [SerializeField] private CanvasGroup interactionGroup;
        private bool _joinTouched;
        public event Action CreateRequested, ContinueRequested, ForgetRequested, RetryRequested;
        public event Action<string> JoinRequested;

        private void Awake()
        {
            createButton.onClick.AddListener(() => CreateRequested?.Invoke());
            continueButton.onClick.AddListener(() => ContinueRequested?.Invoke());
            forgetButton.onClick.AddListener(() => ForgetRequested?.Invoke());
            retryButton.onClick.AddListener(() => RetryRequested?.Invoke());
            joinButton.onClick.AddListener(Join);
            joinInput.onValueChanged.AddListener(_ => { if (_joinTouched) Validate(); });
        }

        private void Join()
        {
            _joinTouched = true;
            if (!Validate()) return;
            JoinRequested?.Invoke(MultiplayerSession.NormalizeJoinCode(joinInput.text));
        }

        private bool Validate()
        {
            var error = ClientInputValidation.JoinCodeError(joinInput.text);
            validationText.text = error ?? "";
            return error == null;
        }

        public void SetInteractionEnabled(bool enabled)
        {
            interactionGroup.interactable = interactionGroup.blocksRaycasts = enabled;
        }

        public void FocusForget() => forgetButton.Select();

        public void Refresh(bool hasSave, bool hasIssuedSession, bool busy, string error)
        {
            var canChoose = !hasSave && !hasIssuedSession;
            createButton.gameObject.SetActive(canChoose);
            joinForm.SetActive(canChoose);
            continueButton.gameObject.SetActive(hasSave && !hasIssuedSession);
            retryButton.gameObject.SetActive(hasIssuedSession);
            forgetButton.gameObject.SetActive(hasSave || hasIssuedSession);
            createButton.interactable = joinButton.interactable = continueButton.interactable = retryButton.interactable = !busy;
            savedSessionText.text = hasSave || hasIssuedSession
                ? "Your world is saved on this device. Continue with the same player. Forget permanently removes local access; it does not free the server slot."
                : "Create a persistent world, or join using an invitation code.";
            if (!string.IsNullOrEmpty(error)) validationText.text = error;
            else if (_joinTouched && canChoose) Validate();
            else validationText.text = "";
        }
    }
}
