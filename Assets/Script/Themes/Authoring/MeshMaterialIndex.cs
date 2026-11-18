using System;
using UnityEngine;

namespace YARG.Themes
{
    [Serializable]
    public struct MeshMaterialIndex
    {
        public MeshRenderer Mesh;
        public int MaterialIndex;
    }

    [Serializable]
    public struct MeshEmissionMaterialIndex
    {
        public MeshRenderer Mesh;
        public int MaterialIndex;

        [Space]
        public float EmissionMultiplier;
        public float EmissionAddition;
        [Tooltip("Lane-color multiplier. Zero uses full color for legacy themes.")]
        public float ColorMultiplier;
    }
}