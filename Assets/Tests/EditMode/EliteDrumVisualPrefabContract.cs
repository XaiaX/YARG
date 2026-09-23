using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class EliteDrumVisualPrefabContract
    {
        private const string LanePrefabPath = "Assets/Prefabs/Gameplay/Visual/TrackElements/Lane.prefab";

        [Test]
        public void LanePrefab_UsesLaneElementAndRequiredGeometry()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LanePrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Could not load {LanePrefabPath}.");

            var laneType = ProductionType("YARG.Gameplay.Visuals.LaneElement");
            Assert.That(prefab.GetComponent(laneType), Is.Not.Null,
                "Elite adapter requires the production LaneElement component.");
            Assert.That(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Not.Empty,
                "Lane prefab must retain its production skinned mesh visual.");
            Assert.That(prefab.transform.GetComponentsInChildren<Transform>(true)
                .Any(child => child.name == "Mesh Transform"), Is.True,
                "Lane prefab must retain the mesh transform used by LaneElement.");
        }

        [Test]
        public void LanePrefab_CanBeAssignedToProductionPool()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LanePrefabPath);
            Assert.That(prefab, Is.Not.Null, $"Could not load {LanePrefabPath}.");

            var poolType = ProductionType("YARG.Gameplay.Pool");
            var poolObject = new GameObject("EliteDrumVisualPoolContract");
            try
            {
                var pool = poolObject.AddComponent(poolType);
                var serialized = new SerializedObject(pool);
                serialized.FindProperty("_prewarmAmount").intValue = 0;
                serialized.FindProperty("_objectCap").intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                poolType.GetMethod("SetPrefabAndReset").Invoke(pool, new object[] { prefab });

                var assignedPrefab = poolType.GetProperty("Prefab").GetValue(pool);
                Assert.That(assignedPrefab, Is.EqualTo(prefab));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(poolObject);
            }
        }

        private static Type ProductionType(string name)
        {
            var type = Type.GetType(name + ", Assembly-CSharp");
            if (type is null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(name);
                    if (type is not null) break;
                }
            }

            Assert.That(type, Is.Not.Null, $"Production type {name} is missing from loaded Unity assemblies.");
            return type;
        }
    }
}
