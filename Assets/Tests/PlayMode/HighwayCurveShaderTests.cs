// pattern: Imperative Shell
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YARG.Tests.PlayMode
{
    public sealed class HighwayCurveShaderTests
    {
        [TestCase(-3f)]
        [TestCase(-.5f)]
        [TestCase(0f)]
        [TestCase(.5f)]
        [TestCase(3f)]
        [TestCase(3f, 8f)]
        public void CenterVerticesFollowContinuousCircularProjection(float curve, float width = 2f)
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True, "Run this graphics test with a graphics device.");
            var shader = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/Tests/PlayMode/HighwayCurveProbe.compute"));
            var samples = new[] { -.00101f, -.00099f, -.0006266f, 0, .0006266f, .00099f, .00101f }
                .Concat(new[] { -width * .5f, width * .5f })
                .Select(x => new Vector4(x, 100.04f, 0, 1)).ToArray();
            using var input = new ComputeBuffer(samples.Length, 16);
            using var output = new ComputeBuffer(samples.Length, 16);
            try
            {
                input.SetData(samples);
                shader.SetInt("_YargHighwaysN", 1);
                shader.SetFloats("_YargHighwayOrigins", new float[32]);
                shader.SetFloats("_YargHighwayWidths", Enumerable.Repeat(width, 32).ToArray());
                shader.SetFloats("_YargCurveFactors", Enumerable.Repeat(curve, 32).ToArray());
                shader.SetMatrixArray("_YargCamViewMatrices", Enumerable.Repeat(Matrix4x4.identity, 32).ToArray());
                shader.SetMatrixArray("_YargCamProjMatrices", Enumerable.Repeat(Matrix4x4.identity, 32).ToArray());
                shader.SetBuffer(0, "_Samples", input);
                shader.SetBuffer(0, "_Results", output);
                shader.Dispatch(0, samples.Length, 1, 1);
                var result = new Vector4[samples.Length];
                output.GetData(result);
                for (int i = 0; i < samples.Length; i++)
                {
                    double radius = (Math.Sign(curve) * 15 - curve * (13d / 3)) * width * .5;
                    double expected = curve == 0 ? samples[i].x :
                        Math.Sin(samples[i].x / radius) * (radius + samples[i].y - 100);
                    // Keep the original absolute precision check at the small-angle branch.
                    // Metal's float sin approximation at a highway edge scales with |x|.
                    double tolerance = Math.Max(.000001, Math.Abs(samples[i].x) * .000003);
                    Assert.That(result[i].x, Is.EqualTo(expected).Within(tolerance), $"curve={curve}, x={samples[i].x}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(shader); }
        }
    }
}
