// pattern: Imperative Shell
using System;
using System.Reflection;
using NUnit.Framework;

namespace YARG.Tests.EditMode
{
    public sealed class GameplayFinishNotificationsEditModeTests
    {
        [TestCase(0.99f, "ThatsAll")]
        [TestCase(1f, "YouMadeIt")]
        [TestCase(2f, "GoodWork")]
        [TestCase(3f, "GoodJob")]
        [TestCase(4f, "GreatJob")]
        [TestCase(5.99f, "NailedIt")]
        [TestCase(6f, "CrushedIt")]
        public void FinishMessageUsesEarnedStars(float stars, string key)
        {
            var method = HudType("TextNotifications").GetMethod("GetSongFinishKey",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method.Invoke(null, new object[] { stars }), Is.EqualTo(key));
        }

        [TestCase("FullCombo")]
        [TestCase("StrongFinish")]
        [TestCase(null)]
        public void FinishMessageFollowsExistingFinishAndSkipsQueuedMilestones(string previousFinish)
        {
            var queueType = HudType("TextNotificationQueue");
            var queue = Activator.CreateInstance(queueType);
            var notificationType = HudType("TextNotification");
            var enqueue = queueType.GetMethod("Enqueue");
            void Queue(string type) => enqueue.Invoke(queue, new[]
            {
                Activator.CreateInstance(notificationType, Enum.Parse(HudType("TextNotificationType"), type), type),
            });
            string Next() => (string) notificationType.GetProperty("Text")
                .GetValue(queueType.GetMethod("Dequeue").Invoke(queue, null));

            Queue("StarPowerReady");
            if (previousFinish != null) Queue(previousFinish);
            Queue("SongFinish");

            if (previousFinish != null) Assert.That(Next(), Is.EqualTo(previousFinish));
            Assert.That(Next(), Is.EqualTo("SongFinish"));
            Assert.That(queueType.GetProperty("Count").GetValue(queue), Is.Zero);
        }

        [TestCase("FullCombo", true)]
        [TestCase("StrongFinish", true)]
        [TestCase("StarPowerReady", false)]
        [TestCase("VocalPhraseResult", false)]
        public void OnlyFinishAwardsKeepTheirAnimationWhenThePerformanceMessageArrives(string active, bool keep)
        {
            var method = HudType("TextNotificationQueue").GetMethod("IsFinishAward",
                BindingFlags.Static | BindingFlags.NonPublic);
            bool result = (bool) method.Invoke(null, new[] { Enum.Parse(HudType("TextNotificationType"), active) });
            Assert.That(result, Is.EqualTo(keep));
        }

        private static Type HudType(string name) => Type.GetType($"YARG.Gameplay.HUD.{name}, Assembly-CSharp", true);
    }
}
