using System;
using System.Reflection;
using NUnit.Framework;
using YARG.Core.Game;

namespace YARG.Tests.EditMode
{
    public sealed class EliteLayoutPlayerSelectionTests
    {
        [Test]
        public void MissingLayoutReferenceUsesCompactWithoutRewritingRecoverableSelection()
        {
            var profile = new YargProfile { EliteDrumsLayout = Guid.NewGuid() };
            var reference = profile.EliteDrumsLayout;
            var playerType = Type.GetType("YARG.Player.YargPlayer, Assembly-CSharp", true);
            var bindingsType = Type.GetType("YARG.Input.ProfileBindings, Assembly-CSharp", true);
            var constructor = playerType.GetConstructor(new[] { typeof(YargProfile), bindingsType });
            var player = constructor.Invoke(new object[] { profile, null });
            try
            {
                playerType.GetMethod("RefreshPresets").Invoke(player, null);
                var effective = (EliteDrumsLayoutPreset) playerType.GetProperty("EliteDrumsLayout").GetValue(player);
                Assert.That(effective.Id, Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
                Assert.That(profile.EliteDrumsLayout, Is.EqualTo(reference));
                Assert.That(profile.ReplayEliteDrumsLayout.Id, Is.EqualTo(EliteDrumsLayoutPreset.CompactId));
                Assert.That(profile.ReplayEliteDrumsLayout, Is.Not.SameAs(effective));
                effective.Slots[0].Drum = null;
                Assert.That(profile.ReplayEliteDrumsLayout.Slots[0].Drum, Is.EqualTo("snare"));
            }
            finally
            {
                ((IDisposable) player).Dispose();
            }
        }
    }
}
