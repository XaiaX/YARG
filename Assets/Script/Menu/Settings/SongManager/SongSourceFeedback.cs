using TMPro;
using UnityEngine;

namespace YARG.Menu.Settings
{
    public sealed class SongSourceFeedback : MonoBehaviour
    {
        private void Update()
        {
            var text = GetComponent<TextMeshProUGUI>();
            text.text = SongSourceActions.Feedback;
        }
    }
}
