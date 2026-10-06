using System;
using UnityEngine;
using UnityEngine.UI;

namespace ConquestusTerrarum.Client.UI
{
    // A presentation-only confirmation. The coordinator decides what confirming does.
    public sealed class ConfirmationDialog : MonoBehaviour
    {
        [SerializeField] private Button confirmButton, cancelButton;
        public event Action Confirmed, Cancelled;

        private void Awake()
        {
            confirmButton.onClick.AddListener(() => { Hide(); Confirmed?.Invoke(); });
            cancelButton.onClick.AddListener(() => { Hide(); Cancelled?.Invoke(); });
        }

        public void Show()
        {
            gameObject.SetActive(true);
            cancelButton.Select();
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
