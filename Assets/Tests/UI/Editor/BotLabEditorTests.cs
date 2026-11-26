using System.Reflection;
using BlackWhale.UI.Dev;
using BlackWhale.UI.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace BlackWhale.UI.Tests
{
    public sealed class BotLabEditorTests
    {
        [Test]
        public void BotLabWindow_CreationAndWindowOpen()
        {
            var window = ScriptableObject.CreateInstance<BotLabWindow>();
            try
            {
                Assert.IsNotNull(window);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var settingsField = typeof(BotLabWindow).GetField("settings", flags);
                Assert.IsNotNull(settingsField);
                var settings = (BotExperimentSettings)settingsField.GetValue(window);
                Assert.IsNotNull(settings);
                Assert.AreEqual(4, settings.bots.Length);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void BotLabWindow_SimulationLifecycleAndOnGUI()
        {
            var window = ScriptableObject.CreateInstance<BotLabWindow>();
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var settings = (BotExperimentSettings)typeof(BotLabWindow).GetField("settings", flags).GetValue(window);
                settings.repetitions = 1;
                settings.firstSeed = 42;

                var startMethod = typeof(BotLabWindow).GetMethod("StartExperiment", flags);
                Assert.IsNotNull(startMethod);
                startMethod.Invoke(window, null);

                var batchField = typeof(BotLabWindow).GetField("batch", flags);
                var batch = (BotExperimentBatch)batchField.GetValue(window);
                Assert.IsNotNull(batch, "Batch should be initialized after StartExperiment");
                Assert.IsTrue(batch.Running, "Batch should be in Running state");

                // Step simulation until run 1 completes
                int steps = 0;
                while (batch.Running && steps++ < 20000)
                {
                    batch.Step();
                }

                Assert.AreEqual(1, batch.Completed, "1 run should complete");
                Assert.AreEqual(0, batch.Results.runs.Count == 1 ? 0 : -1);

                // Check Stop method
                var stopMethod = typeof(BotLabWindow).GetMethod("Stop", flags);
                stopMethod.Invoke(window, null);
                Assert.IsFalse(batch.Running, "Batch should not be running after Stop");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void BotLabWindow_StopMidwayPreservesCompletedRuns()
        {
            var window = ScriptableObject.CreateInstance<BotLabWindow>();
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var settings = (BotExperimentSettings)typeof(BotLabWindow).GetField("settings", flags).GetValue(window);
                settings.repetitions = 3;
                settings.firstSeed = 101;

                var startMethod = typeof(BotLabWindow).GetMethod("StartExperiment", flags);
                startMethod.Invoke(window, null);

                var batchField = typeof(BotLabWindow).GetField("batch", flags);
                var batch = (BotExperimentBatch)batchField.GetValue(window);

                // Run until at least 1 completed, then stop
                while (batch.Running && batch.Completed < 1)
                {
                    batch.Step();
                }

                Assert.AreEqual(1, batch.Completed);
                var stopMethod = typeof(BotLabWindow).GetMethod("Stop", flags);
                stopMethod.Invoke(window, null);

                Assert.AreEqual(1, batch.Completed, "Stopping midway preserves completed runs only");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }
}
