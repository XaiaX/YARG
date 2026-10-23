using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class FilePickerOrderingTests
    {
        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        [TestCase(true, "alpha.dta,zebra.dta,Alpha Folder,Zebra Folder")]
        [TestCase(false, "Alpha Folder,Zebra Folder,alpha.dta,zebra.dta")]
        public void EntriesAreAlphabeticalWithinRequestedGroups(bool filesFirst, string expected)
        {
            var entry = Production("SimpleFileBrowser.FileSystemEntry");
            object Make(string name, bool directory) => Activator.CreateInstance(entry, name, name, ".dta", directory);
            var entries = new[] { Make("Zebra Folder", true), Make("zebra.dta", false),
                Make("Alpha Folder", true), Make("alpha.dta", false) };
            var compare = Production("SimpleFileBrowser.FileBrowser").GetMethod("CompareEntries",
                BindingFlags.Static | BindingFlags.NonPublic);
            Array.Sort(entries, (first, second) => (int)compare.Invoke(null, new[] { first, second, filesFirst }));
            Assert.That(string.Join(",", entries.Select(value => entry.GetField("Name").GetValue(value))), Is.EqualTo(expected));
        }

        [Test]
        public void ExistingLoadDialogsDefaultToDirectoryFirst()
        {
            var show = Production("SimpleFileBrowser.FileBrowser").GetMethod("ShowLoadDialog");
            var option = show.GetParameters().Single(parameter => parameter.Name == "filesFirst");
            Assert.That(option.IsOptional, Is.True);
            Assert.That(option.DefaultValue, Is.False);
        }
    }
}
