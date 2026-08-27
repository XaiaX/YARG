using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using YARG.Core.Logging;

namespace YARG
{
    public class UnityMainThreadCallback : MonoBehaviour
    {
        private static readonly Queue<Action> CallbackQueue = new();

        private void Update()
        {
            using var diagnostics = PerformanceDiagnostics.Scope(PerformanceDiagnostics.UnityMainThreadCallbackUpdateMarker);
            long startTicks = PerformanceDiagnostics.Timestamp();
            int queueBefore;
            lock (CallbackQueue)
            {
                queueBefore = CallbackQueue.Count;
            }

            PerformanceDiagnostics.CallbackQueueSample(queueBefore, queueBefore);
            try
            {
                while (true)
                {
                    Action action;
                    lock (CallbackQueue)
                    {
                        if (CallbackQueue.Count == 0)
                        {
                            break;
                        }
                        action = CallbackQueue.Dequeue();
                    }

                    PerformanceDiagnostics.CallbackDequeued();
                    try
                    {
                        action.Invoke();
                    }
                    catch (Exception e)
                    {
                        PerformanceDiagnostics.CallbackException();
                        YargLogger.LogException(e, "Failed to run main thread callbacks");
                    }
                }
            }
            finally
            {
                int queueAfter;
                lock (CallbackQueue)
                {
                    queueAfter = CallbackQueue.Count;
                }
                PerformanceDiagnostics.CallbackQueueSample(queueBefore, queueAfter);
                PerformanceDiagnostics.CallbackDrainTicks(PerformanceDiagnostics.ElapsedTicks(startTicks));
            }
        }

        public static void QueueEvent(Action action)
        {
            lock (CallbackQueue)
            {
                CallbackQueue.Enqueue(action);
            }
        }
    }
}
