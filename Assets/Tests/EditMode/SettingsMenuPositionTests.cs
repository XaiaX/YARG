using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class SettingsMenuPositionTests
    {
        [TestCase(4, 8, 4)]
        [TestCase(7, 7, 6)]
        [TestCase(7, 3, 2)]
        [TestCase(0, 1, 0)]
        [TestCase(7, 0, null)]
        [TestCase(null, 8, null)]
        public void RestoresNearestRemainingSelection(int? previous, int count, int? expected)
        {
            var menu = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("YARG.Menu.Settings.SettingsMenu"))
                .First(type => type != null);
            var restore = menu.GetMethod("GetRestoredSelectionIndex", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(restore.Invoke(null, new object[] { previous, count }), Is.EqualTo(expected));
        }
    }
}
