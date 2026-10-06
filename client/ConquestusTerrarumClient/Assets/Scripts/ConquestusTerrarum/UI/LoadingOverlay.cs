using TMPro;
using UnityEngine;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class LoadingOverlay : MonoBehaviour
    {
        [SerializeField] private TMP_Text statusText;
        public void Refresh(bool loading, string status)
        {
            gameObject.SetActive(loading);
            statusText.text = status;
        }
    }
}
