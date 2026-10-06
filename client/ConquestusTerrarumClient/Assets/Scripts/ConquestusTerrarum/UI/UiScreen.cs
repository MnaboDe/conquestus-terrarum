using UnityEngine;

namespace ConquestusTerrarum.Client.UI
{
    public abstract class UiScreen : MonoBehaviour
    {
        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
    }
}
