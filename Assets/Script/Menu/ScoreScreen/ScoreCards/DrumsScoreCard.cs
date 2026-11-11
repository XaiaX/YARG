using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using YARG.Core;
using YARG.Core.Engine.Drums;
using YARG.Input;
using YARG.Localization;

namespace YARG.Menu.ScoreScreen
{
    public class DrumsScoreCard : ScoreCard<DrumsStats>
    {
        [Space]
        [SerializeField]
        private TextMeshProUGUI _overhits;
        [SerializeField]
        private StatInfo _statInfoPrefab;

        public override void SetCardContents()
        {
            base.SetCardContents();

            // Set background icon
            _instrumentIcon.sprite = Addressables
                .LoadAssetAsync<Sprite>($"InstrumentIcons[drums]")
                .WaitForCompletion();

            SetDrumStatRows();
        }

        private void SetDrumStatRows()
        {
            _overhits.text = ColorizePrimary(Stats.Overhits);

            var overhitsRow = _overhits.transform.parent;
            var rows = overhitsRow.parent;
            if (Stats.MisarticulatedHiHats > 0)
            {
                var articulationRow = CreateStatRow(rows, Localize.Key("Menu.ScoreScreen.MisarticulatedHiHats"),
                    ColorizePrimary(Stats.MisarticulatedHiHats));
                articulationRow.transform.SetSiblingIndex(overhitsRow.GetSiblingIndex());
            }
            var bindings = BindingCollection.CreateGameplayBindings(Player.Profile.CurrentInstrument.ToNativeGameMode());

            foreach (var binding in bindings)
            {
                if (Stats.OverhitsByAction.TryGetValue(binding.Action, out int count))
                {
                    string key = Player.Profile.LeftyFlip ? binding.NameLefty : binding.Name;
                    CreateStatRow(rows, "<space=20px>" + Localize.Key("Bindings", key), ColorizePrimary(count));
                }
            }

            if (Stats.TotalGhosts > 0 || Stats.TotalAccents > 0)
            {
                CreateStatRow(rows, Localize.Key("Menu.ScoreScreen.GhostsHitCorrectly"),
                    DynamicsCount(Stats.GhostsHit, Stats.TotalGhosts));
                CreateStatRow(rows, Localize.Key("Menu.ScoreScreen.AccentsHitCorrectly"),
                    DynamicsCount(Stats.AccentsHit, Stats.TotalAccents));
                CreateStatRow(rows, Localize.Key("Menu.ScoreScreen.BaseDynamicsBonus"),
                    ColorizePrimary(Stats.DynamicsBonus.ToString("N0")));
            }
        }

        private string DynamicsCount(int correct, int total) => $"{ColorizePrimary(correct)} / {ColorizeSecondary(total)}";

        private StatInfo CreateStatRow(Transform parent, string label, string value)
        {
            var info = Instantiate(_statInfoPrefab, parent);
            info.Label.text = label;
            info.Value.text = value;
            return info;
        }

    }
}
