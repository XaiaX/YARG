using System;
using System.Linq.Expressions;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class DifficultySelectSelectionLifecycleTests
    {
        private const string PREFAB_PATH = "Assets/Prefabs/Menu/DifficultySelect/DifficultySelectMenu.prefab";

        private static readonly Type MenuType = ProductionType(
            "YARG.Menu.DifficultySelect.DifficultySelectMenu");
        private static readonly Type NavigationGroupType = ProductionType(
            "YARG.Menu.Navigation.NavigationGroup");
        private static readonly Type NavigatableType = ProductionType(
            "YARG.Menu.Navigation.RuntimeNavigatable");

        [Test]
        public void OnDisable_RemovesOnlyDifficultyMenuSelectionCallback_EvenWhenRepeated()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Assert.That(prefab, Is.Not.Null, $"Missing production prefab at {PREFAB_PATH}.");

            var inactiveParent = new GameObject("Inactive Difficulty Select Test Parent");
            inactiveParent.SetActive(false);
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, inactiveParent.transform);
                var menu = instance.GetComponent(MenuType);
                Assert.That(menu, Is.Not.Null);

                var navGroupField = MenuType.GetField("_navGroup",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(navGroupField, Is.Not.Null);
                var navGroup = navGroupField.GetValue(menu);
                Assert.That(navGroup, Is.Not.Null);

                var selectionChanged = NavigationGroupType.GetEvent("SelectionChanged");
                Assert.That(selectionChanged, Is.Not.Null);
                var callback = Delegate.CreateDelegate(selectionChanged.EventHandlerType, menu,
                    MenuType.GetMethod("UpdateForSelectionChanged",
                        BindingFlags.Instance | BindingFlags.NonPublic));
                var unrelatedSubscriber = NoOpSubscriber(selectionChanged.EventHandlerType);

                selectionChanged.AddEventHandler(navGroup, callback);
                selectionChanged.AddEventHandler(navGroup, unrelatedSubscriber);

                InvokeLifecycle(menu, "OnDisable");
                InvokeLifecycle(menu, "OnDisable");

                var backingField = NavigationGroupType.GetField("SelectionChanged",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(backingField, Is.Not.Null,
                    "Expected the event's backing field to expose its current invocation list.");
                var remaining = (Delegate) backingField.GetValue(navGroup);
                Assert.That(remaining, Is.Not.Null);
                Assert.That(remaining.GetInvocationList(), Has.Length.EqualTo(1));
                Assert.That(remaining.GetInvocationList()[0], Is.EqualTo(unrelatedSubscriber),
                    "Disabling the menu must remove only its own callback.");
            }
            finally
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);
                UnityEngine.Object.DestroyImmediate(inactiveParent);
            }
        }

        [Test]
        public void RuntimeNavigatable_DefaultInputGateAllowsInput_AndExplicitGateKeepsProgrammaticSelection()
        {
            var gameObject = new GameObject("Runtime Navigatable Test");
            try
            {
                var navigatable = gameObject.AddComponent(NavigatableType);
                var inputAllowed = NavigatableType.GetProperty("InputAllowed");
                var confirmCallback = NavigatableType.GetField("ConfirmCallback");
                var setSelected = NavigatableType.GetMethod("SetSelected");
                var confirm = NavigatableType.GetMethod("Confirm");
                Assert.That(inputAllowed, Is.Not.Null);
                Assert.That(confirmCallback, Is.Not.Null);
                Assert.That(setSelected, Is.Not.Null);
                Assert.That(confirm, Is.Not.Null);
                Assert.That(inputAllowed.GetValue(navigatable), Is.Null,
                    "The default must not require an owner to install an input gate.");

                int confirmCount = 0;
                confirmCallback.SetValue(navigatable, (Action) (() => confirmCount++));
                var selectionOriginType = ProductionType("YARG.Menu.Navigation.SelectionOrigin");
                object Origin(string name) => Enum.Parse(selectionOriginType, name);

                setSelected.Invoke(navigatable, new[] { (object) true, Origin("Mouse") });
                confirm.Invoke(navigatable, null);
                Assert.That(GetSelected(navigatable), Is.True);
                Assert.That(confirmCount, Is.EqualTo(1));

                setSelected.Invoke(navigatable, new[] { (object) false, Origin("Programmatically") });
                inputAllowed.SetValue(navigatable, (Func<bool>) (() => false));
                setSelected.Invoke(navigatable, new[] { (object) true, Origin("Navigation") });
                confirm.Invoke(navigatable, null);
                Assert.That(GetSelected(navigatable), Is.False,
                    "A false owner gate must reject user-driven selection.");
                Assert.That(confirmCount, Is.EqualTo(1),
                    "A false owner gate must reject confirm input.");

                setSelected.Invoke(navigatable,
                    new[] { (object) true, Origin("Programmatically") });
                Assert.That(GetSelected(navigatable), Is.True,
                    "Programmatic selection must remain available while input is gated.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static bool GetSelected(object navigatable)
        {
            var selected = NavigatableType.GetProperty("Selected");
            Assert.That(selected, Is.Not.Null);
            return (bool) selected.GetValue(navigatable);
        }

        private static Delegate NoOpSubscriber(Type delegateType)
        {
            var parameters = Array.ConvertAll(delegateType.GetMethod("Invoke").GetParameters(),
                parameter => Expression.Parameter(parameter.ParameterType, parameter.Name));
            return Expression.Lambda(delegateType, Expression.Empty(), parameters).Compile();
        }

        private static void InvokeLifecycle(object instance, string name)
        {
            var method = MenuType.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing lifecycle method {name}.");
            method.Invoke(instance, null);
        }

        private static Type ProductionType(string name)
        {
            var type = Type.GetType(name + ", Assembly-CSharp");
            if (type is null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(name);
                    if (type is not null)
                        break;
                }
            }

            Assert.That(type, Is.Not.Null,
                $"Production type {name} is missing from loaded Unity assemblies.");
            return type;
        }
    }
}
