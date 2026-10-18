using YARG.Core.Game;
using YARG.Themes;

namespace YARG.Settings.Preview
{
    /// <summary>Synthetic preview appearance, independent of chart conversion and color identity.</summary>
    public readonly struct ElitePreviewDescriptor
    {
        public EliteDrumsColorRole Role { get; }
        public int Fret { get; }
        public ThemeNoteType ModelType { get; }
        public bool IsBar { get; }
        public float Width { get; }
        public float Offset { get; }
        public string DedicatedLaneSource { get; }

        public ElitePreviewDescriptor(EliteDrumsColorRole role, int fret, ThemeNoteType modelType,
            bool isBar = false, float width = 1f, float offset = 0f)
            : this(role, fret, modelType, isBar, width, offset, null)
        {
        }

        public ElitePreviewDescriptor(EliteDrumsColorRole role, int fret, ThemeNoteType modelType,
            bool isBar, float width, float offset, string dedicatedLaneSource)
        {
            Role = role;
            Fret = fret;
            ModelType = modelType;
            IsBar = isBar;
            Width = width;
            Offset = offset;
            DedicatedLaneSource = dedicatedLaneSource;
        }
    }

    public class FakeNoteData
    {
        public double Time;
        public ElitePreviewDescriptor? EliteDescriptor;

        public int Fret;
        public bool CenterNote;
        public ThemeNoteType NoteType;

        // Overrides the global ForceStarPowerNotes toggle for this note in both
        // directions: true renders star power colors, false renders regular
        // colors, null follows the toggle. Used by the lane spotlight so the
        // edited color field (star power or not) is what's on screen.
        public bool? ForceStarPower;

        // When true, the note renders with the Miss color instead of its
        // normal color. Used by the miss-note spotlight.
        public bool ForceMiss;

        // Set for simultaneous Elite drum/cymbal notes sharing a saved lane when
        // offset paired-gem presentation is enabled.
        public bool OffsetElitePair;
        public FakeNoteData ElitePairPartner;
        public bool EliteHandFlam;
        public bool EliteFlatFlam;
    }
}