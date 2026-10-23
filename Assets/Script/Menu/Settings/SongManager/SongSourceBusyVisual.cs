using UnityEngine;
using UnityEngine.UI;

namespace YARG.Menu.Settings
{
    public sealed class SongSourceBusyVisual : MonoBehaviour
    {
        private void Update()
        {
            foreach (var button in GetComponentsInChildren<Button>())
                button.interactable = !SongSourceActions.Busy && !SongSourceActions.IsRefreshing();
        }
    }
}
