using NUnit.Framework;

namespace YARG.Tests.PlayMode
{
    public sealed class FretArrayCenteringPlayModeTests
    {
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void StockClassicCenterFormulaRemainsEquivalent(int lanes)
        {
            for (int lane = 0; lane < lanes; lane++)
            {
                float previous = 2f / lanes * lane - 1f + 1f / lanes;
                float centered = 2f / lanes * (lane + 0.5f) - 1f;
                Assert.That(centered, Is.EqualTo(previous).Within(0.000001f));
            }
        }
    }
}
