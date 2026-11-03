// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using YARG.Core;
using YARG.Core.Game;

namespace YARG.Player
{
    internal static class ProfileFlamLayoutMigration
    {
        // Settings load after profiles' initial sanitized save. Resolve the pending
        // values together, then persist once without changing profile order.
        internal static int Apply(IReadOnlyList<YargProfile> profiles, DrumFlamLayout legacy, Action persist)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            if (persist == null) throw new ArgumentNullException(nameof(persist));
            int changed = 0;
            foreach (var profile in profiles)
                if (profile.MigrateFlamLayout(legacy)) changed++;
            if (changed > 0) persist();
            return changed;
        }
    }
}
