using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class ErrorPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button dismissButton;
        public event Action DismissRequested;
        private void Awake() => dismissButton.onClick.AddListener(() => DismissRequested?.Invoke());
        public void Refresh(string message)
        {
            gameObject.SetActive(!string.IsNullOrEmpty(message));
            messageText.text = message ?? "";
        }
    }
}
