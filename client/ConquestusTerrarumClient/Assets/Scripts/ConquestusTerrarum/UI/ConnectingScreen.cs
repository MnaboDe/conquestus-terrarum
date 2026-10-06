using TMPro;
using UnityEngine;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class ConnectingScreen : UiScreen
    {
        [SerializeField] private TMP_Text statusText;
        public void Refresh(string status) => statusText.text = status;
    }
}
