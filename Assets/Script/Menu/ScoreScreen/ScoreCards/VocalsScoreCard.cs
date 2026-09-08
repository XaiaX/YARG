using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core;
using YARG.Core.Engine.Vocals;
using YARG.Helpers.Extensions;

namespace YARG.Menu.ScoreScreen
{
    public class VocalsScoreCard : ScoreCard<VocalsStats>
    {
        public override void SetCardContents()
        {
            base.SetCardContents();

            // Party Vocals uses the same part-count icon convention as the gameplay HUD.
            string iconName = Player.Profile.GameMode == GameMode.PartyVocals
                ? GlobalVariables.State.CurrentSong.VocalsCount switch
                {
                    >= 3 => "harmVocals",
                    2 => "twoVocals",
                    _ => "vocals",
                }
                : Player.Profile.CurrentInstrument.ToResourceName();
            _instrumentIcon.sprite = Addressables
                .LoadAssetAsync<Sprite>($"InstrumentIcons[{iconName}]")
                .WaitForCompletion();
        }

        // Vocals has no advanced stats
        public override void SetAdvancedStatsShown(bool showAdvanced)
        {
        }
    }
}