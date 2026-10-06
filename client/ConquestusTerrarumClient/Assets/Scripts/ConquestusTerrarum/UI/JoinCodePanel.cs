using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ConquestusTerrarum.Client.UI
{
    public sealed class JoinCodePanel : MonoBehaviour
    {
        [SerializeField] private TMP_Text codeText;
        [SerializeField] private Button copyButton;
        private string _code;
        private void Awake() => copyButton.onClick.AddListener(() => GUIUtility.systemCopyBuffer = _code ?? "");
        public void Refresh(bool waiting, string code)
        {
            _code = code;
            gameObject.SetActive(waiting && !string.IsNullOrEmpty(code));
            codeText.text = code ?? "";
        }
    }
}
