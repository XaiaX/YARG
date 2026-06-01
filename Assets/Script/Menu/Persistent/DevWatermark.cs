using TMPro;
using UnityEngine;

namespace YARG.Menu.Persistent
{
    public class DevWatermark : MonoBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _watermarkText;

        private void Start()
        {
#if UNITY_EDITOR
            _watermarkText.text = $"<b>YARG {GlobalVariables.Instance.CurrentVersion}</b> Unity Editor ({SystemInfo.graphicsDeviceType})";
#elif YARG_TEST_BUILD
            // FORK-LOCAL (Party Vocals prototype branding): simplified from the upstream
            // "YARG {version} Development Build (...)" so a prototype build reads just
            // "{version} Build (...)". {version} is GlobalVariables.CurrentVersion, set to
            // "Party Vocals Prototype" (see GlobalVariables.cs). Revert to the upstream string
            // if this branch is ever merged. See docs/party-vocals-prototype-build-branding.md.
            _watermarkText.text = $"<b>{GlobalVariables.Instance.CurrentVersion}</b> Build ({SystemInfo.graphicsDeviceType})";
#elif YARG_NIGHTLY_BUILD
            _watermarkText.text = $"<b>YARG {GlobalVariables.Instance.CurrentVersion}</b> Nightly Build ({SystemInfo.graphicsDeviceType})";
#else
            gameObject.SetActive(false);
#endif
        }
    }
}
