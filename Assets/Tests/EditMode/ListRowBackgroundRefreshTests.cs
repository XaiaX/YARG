// pattern: Imperative Shell
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;

namespace YARG.Tests.EditMode
{
    public sealed class ListRowBackgroundRefreshTests
    {
        // HistoryView exercises the inherited ViewObject implementation; SongView
        // exercises its override. No app settings, score database, or prefabs are needed.
        [TestCase(false, "Normal", false, "NormalBackground")]
        [TestCase(false, "Normal", true, "SelectedBackground")]
        [TestCase(false, "Category", false, "CategoryBackground")]
        [TestCase(false, "Category", true, "SelectedBackground")]
        [TestCase(false, "Button", false, null)]
        [TestCase(false, "Button", true, null)]
        [TestCase(true, "Normal", false, "NormalBackground")]
        [TestCase(true, "Normal", true, "SelectedBackground")]
        [TestCase(true, "Category", false, "CategoryBackground")]
        [TestCase(true, "Category", true, "SelectedBackground")]
        [TestCase(true, "Button", false, "_buttonHeaderBackground")]
        [TestCase(true, "Button", true, "SelectedBackground")]
        public void RebindingTheSameBackgroundDoesNotRestartUiComponents(
            bool songView, string style, bool selected, string expectedBackground)
        {
            using var row = new BackgroundRow(songView);
            row.Apply(style, selected);
            row.AssertVisible(expectedBackground, selected);
            foreach (var counter in row.DirtyCallbacks) counter.Count = 0;

            row.Apply(style, selected);

            row.AssertVisible(expectedBackground, selected);
            Assert.That(row.DirtyCallbacks.Sum(counter => counter.Count), Is.Zero,
                "A row rebind must not invalidate already-correct background graphics.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SwitchingStylesAndSelectionReplacesThePreviousBackground(bool songView)
        {
            using var row = new BackgroundRow(songView);
            var states = new (string Style, bool Selected, string Background)[]
            {
                ("Normal", false, "NormalBackground"),
                ("Normal", true, "SelectedBackground"),
                ("Category", false, "CategoryBackground"),
                ("Category", true, "SelectedBackground"),
                ("Button", false, songView ? "_buttonHeaderBackground" : null),
                ("Button", true, songView ? "SelectedBackground" : null),
                ("Normal", false, "NormalBackground")
            };
            foreach (var state in states)
            {
                row.Apply(state.Style, state.Selected);
                row.AssertVisible(state.Background, state.Selected);
            }
            Assert.That(row.DirtyCallbacks.Sum(counter => counter.Count), Is.GreaterThan(0),
                "The counters must observe real graphic invalidations when backgrounds change.");
        }

        private sealed class DirtyCounter
        {
            public int Count;
        }

        private sealed class BackgroundRow : IDisposable
        {
            private readonly GameObject _root = new("background-refresh-test");
            private readonly Component _view;
            private readonly MethodInfo _setBackground;
            private readonly Type _style;
            private readonly Dictionary<string, GameObject> _backgrounds = new();
            private readonly GameObject _gradient;
            public readonly List<DirtyCounter> DirtyCallbacks = new();

            public BackgroundRow(bool songView)
            {
                var type = Production(songView ? "YARG.Menu.MusicLibrary.SongView"
                    : "YARG.Menu.History.HistoryView");
                _view = _root.AddComponent(type);
                _setBackground = type.GetMethod("SetBackground", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(_setBackground, Is.Not.Null);
                _style = _setBackground.GetParameters()[1].ParameterType;
                foreach (string field in new[] { "NormalBackground", "SelectedBackground", "CategoryBackground" })
                    AddBackground(field);
                if (songView)
                {
                    AddBackground("_buttonHeaderBackground");
                    _gradient = CreateObservedObject("gradient");
                    var image = _gradient.GetComponent(Production("UnityEngine.UI.Image"));
                    Field(type, "_trackGradient").SetValue(_view, image);
                }
            }

            private GameObject CreateObservedObject(string name)
            {
                var child = new GameObject(name, typeof(RectTransform));
                child.transform.SetParent(_root.transform);
                var imageType = Production("UnityEngine.UI.Image");
                var image = child.AddComponent(imageType);
                var counter = new DirtyCounter();
                DirtyCallbacks.Add(counter);
                UnityAction onDirty = () => counter.Count++;
                imageType.GetMethod("RegisterDirtyVerticesCallback").Invoke(image, new object[] { onDirty });
                return child;
            }

            private void AddBackground(string field)
            {
                var child = CreateObservedObject(field);
                _backgrounds.Add(field, child);
                Field(_view.GetType(), field).SetValue(_view, child);
            }

            public void Apply(string style, bool selected)
                => _setBackground.Invoke(_view, new[] { (object)selected, Enum.Parse(_style, style) });

            public void AssertVisible(string expected, bool selected)
            {
                foreach (var pair in _backgrounds)
                    Assert.That(pair.Value.activeSelf, Is.EqualTo(pair.Key == expected), pair.Key);
                if (_gradient != null) Assert.That(_gradient.activeSelf, Is.EqualTo(selected), "track gradient");
            }

            public void Dispose() => UnityEngine.Object.DestroyImmediate(_root);
        }

        private static Type Production(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).First(type => type != null);

        private static FieldInfo Field(Type type, string name)
        {
            while (type != null)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                type = type.BaseType;
            }
            throw new MissingFieldException(name);
        }
    }
}
