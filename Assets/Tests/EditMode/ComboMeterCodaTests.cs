using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace YARG.Tests.EditMode
{
    public sealed class ComboMeterCodaTests
    {
        [TestCase(4, 4, 4, 47)]
        [TestCase(2, 2, 4, 17)]
        [TestCase(1, 1, 4, 3)]
        [TestCase(8, 8, 8, 47)]
        [TestCase(8, 4, 8, 47)]
        public void CodaPreservesVisibleComboAndNormalUpdatesResume(int multiplier, int displayMultiplier, int maxMultiplier, int combo)
        {
            var meterType = Type.GetType("YARG.Gameplay.Visuals.ComboMeter, Assembly-CSharp", true);
            var textType = Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro", true);
            var root = new GameObject("Combo meter coda regression");
            root.SetActive(false);
            Material material = null;
            MeshRenderer renderer = null;
            try
            {
                var meter = root.AddComponent(meterType);
                renderer = root.AddComponent<MeshRenderer>();
                var asset = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Gameplay/Track/ComboMeter.mat");
                Assert.That(asset, Is.Not.Null);
                material = new Material(asset);
                renderer.sharedMaterial = material;
                var cache = Array.CreateInstance(textType, 7);
                for (int i = 0; i < cache.Length; i++)
                {
                    var child = new GameObject($"Multiplier {i + 2}");
                    child.SetActive(false);
                    child.transform.SetParent(root.transform);
                    var text = (Behaviour) child.AddComponent(textType);
                    text.enabled = false;
                    cache.SetValue(text, i);
                }
                void Field(string name, object value) => meterType.GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(meter, value);
                Field("_comboMesh", renderer);
                Field("_textCache", cache);
                Field("_multiplierText", cache.GetValue(0));
                void Set(int mul, int display, int count, bool coda) => meterType.GetMethod("SetCombo")
                    .Invoke(meter, new object[] { mul, display, maxMultiplier, count, coda });
                void CheckText(int display)
                {
                    for (int i = 0; i < cache.Length; i++)
                        Assert.That(((Behaviour) cache.GetValue(i)).enabled, Is.EqualTo(display == i + 2));
                }
                // Runtime material instancing emits this warning in EditMode; release the instance below.
                LogAssert.Expect(LogType.Error, "Instantiating material due to calling renderer.material during edit mode. This will leak materials into the scene. You most likely want to use renderer.sharedMaterial instead.");
                Set(multiplier, displayMultiplier, combo, false);
                float expected = renderer.sharedMaterial.GetFloat("_SpriteIndex");
                CheckText(displayMultiplier);
                for (int frame = 0; frame < 3; frame++)
                {
                    Set(1, 1, 0, true);
                    Assert.That(renderer.sharedMaterial.GetFloat("_SpriteIndex"), Is.EqualTo(expected),
                        "BRE must preserve the last visible ring throughout its transition.");
                    CheckText(displayMultiplier);
                }
                Set(2, 2, 13, false);
                Assert.That(renderer.sharedMaterial.GetFloat("_SpriteIndex"), Is.EqualTo(3));
                CheckText(2);
                Set(1, 1, 0, false);
                Assert.That(renderer.sharedMaterial.GetFloat("_SpriteIndex"), Is.Zero);
                CheckText(1);
            }
            finally
            {
                if (renderer != null && renderer.sharedMaterial != material)
                    UnityEngine.Object.DestroyImmediate(renderer.sharedMaterial);
                UnityEngine.Object.DestroyImmediate(root);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
