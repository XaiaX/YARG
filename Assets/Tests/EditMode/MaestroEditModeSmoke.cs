using NUnit.Framework;
using UnityEngine;

namespace YARG.Tests.EditMode
{
    public sealed class MaestroEditModeSmoke
    {
        [Test]
        public void Smoke_Assembly_Loads_And_Runs()
        {
            Assert.That(typeof(GameObject), Is.Not.Null);
        }
    }
}
