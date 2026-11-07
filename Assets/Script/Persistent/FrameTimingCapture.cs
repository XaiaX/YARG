// pattern: Imperative Shell
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using YARG.Core.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YARG.Persistent
{
    /// <summary>Bounded, non-blocking handoff. Formatting and disk writes run off the game thread.</summary>
    public sealed class FrameTimingCapture
    {
        private readonly BlockingCollection<FrameDiagnosticSample> _pending = new(4096);
        private int _droppedRows;
        private const int DEFERRED_CAPACITY = 32768;
        private readonly FrameDiagnosticSample[] _deferred;
        private readonly TaskCompletionSource<bool> _beginWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _deferredCount;
        private bool _completed;
        private volatile string _error;
        public string Path { get; }
        public string Error => _error;
        public int DroppedRows => Volatile.Read(ref _droppedRows);
        public Task Completion { get; }

        public FrameTimingCapture(string path) : this(path, false) { }

        public FrameTimingCapture(string path, bool deferWrites)
        {
            Path = path;
            if (deferWrites) _deferred = new FrameDiagnosticSample[DEFERRED_CAPACITY];
            Completion = Task.Run(async () =>
            {
                if (deferWrites) await _beginWrite.Task;
                Write();
            });
        }

        // Called only by the main-thread owner; never blocks on disk or a full queue.
        public void Add(FrameDiagnosticSample sample)
        {
            if (_deferred != null)
            {
                if (_completed || _deferredCount == _deferred.Length) Interlocked.Increment(ref _droppedRows);
                else _deferred[_deferredCount++] = sample;
            }
            else if (_completed || _error != null || !_pending.TryAdd(sample)) Interlocked.Increment(ref _droppedRows);
        }

        public void Complete()
        {
            if (_completed) return;
            _completed = true;
            _pending.CompleteAdding();
            _beginWrite.TrySetResult(true);
        }

        private IEnumerable<FrameDiagnosticSample> Samples()
        {
            if (_deferred != null)
            {
                for (int i = 0; i < _deferredCount; i++) yield return _deferred[i];
            }
            else
                foreach (var sample in _pending.GetConsumingEnumerable()) yield return sample;
        }

        private void Write()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                using var writer = new StreamWriter(Path, false, new UTF8Encoding(false), 65536);
                writer.WriteLine("schema,frame,monotonicSeconds,wallMs,unityMs,scene,focused,quality,requestedVsync,requestedFps,backgroundFps,effectiveVsync,effectiveTargetFps,width,height,refreshHz,graphicsApi,budgetMs,timingEnabled,timingAvailable,timingFresh,timingFrameStartTimestamp,cpuMs,mainMs,renderMs,presentWaitMs,gpuMs,gc0,gc1,gc2,visualTime,visualDelta,songSpeed,pacingChanged,droppedRows,intervalStartFrame,targetedEnabled,targetedChanged,targetedGeneration,rollScheduleCalls,rollScheduleMs,rollScheduleMaxMs,rollResolveCalls,rollResolveMs,rollResolveMaxMs,laneMapBuildCalls,laneMapBuildMs,laneMapBuildMaxMs,hiHatActivationCalls,hiHatActivationMs,hiHatActivationMaxMs,hiHatDiscoveryCalls,hiHatDiscoveryMs,hiHatDiscoveryMaxMs,poolPrewarmCalls,poolPrewarmMs,poolPrewarmMaxMs,poolCreateCalls,poolCreateMs,poolCreateMaxMs,scheduleNotesExamined,resolveNotesExamined,laneLookups,laneMembers,deadlinesQueued,continuations,noteSpawnCalls,noteSpawnMs,noteSpawnMaxMs,noteSpawnMaxStartSeconds,noteSpawnMaxContext,elementEnableCalls,elementEnableMs,elementEnableMaxMs,elementEnableMaxStartSeconds,elementEnableMaxContext,elementSetActiveCalls,elementSetActiveMs,elementSetActiveMaxMs,elementSetActiveMaxStartSeconds,elementSetActiveMaxContext,elementInitializeCalls,elementInitializeMs,elementInitializeMaxMs,elementInitializeMaxStartSeconds,elementInitializeMaxContext,elementFirstVisualUpdateCalls,elementFirstVisualUpdateMs,elementFirstVisualUpdateMaxMs,elementFirstVisualUpdateMaxStartSeconds,elementFirstVisualUpdateMaxContext,noteGroupInitializeCalls,noteGroupInitializeMs,noteGroupInitializeMaxMs,noteGroupInitializeMaxStartSeconds,noteGroupInitializeMaxContext,materialAccessCalls,materialAccessMs,materialAccessMaxMs,materialAccessMaxStartSeconds,materialAccessMaxContext,noteGroupSetActiveCalls,noteGroupSetActiveMs,noteGroupSetActiveMaxMs,noteGroupSetActiveMaxStartSeconds,noteGroupSetActiveMaxContext,pedalMeshBuildCalls,pedalMeshBuildMs,pedalMeshBuildMaxMs,pedalMeshBuildMaxStartSeconds,pedalMeshBuildMaxContext,gameplayAwakeCalls,gameplayAwakeMs,gameplayAwakeMaxMs,gameplayAwakeMaxStartSeconds,gameplayAwakeMaxContext,gameplayManagerDiscoveryCalls,gameplayManagerDiscoveryMs,gameplayManagerDiscoveryMaxMs,gameplayManagerDiscoveryMaxStartSeconds,gameplayManagerDiscoveryMaxContext,firstElementEnables,firstNoteGroupInitializations,gcMetricsEnabled,profilerFrame,gcAvailableMask,gcSampledMask,gcAllocatedBytes,gcMainAllocationCalls,gcUsedBytes,gcReservedBytes,gcIncrementalMs,gcCollectMs,timingCpuTimerFrequency,timingFirstSubmitTimestamp,timingPresentCalledTimestamp,timingFrameCompleteTimestamp,renderMetricsEnabled,renderProfilerFrame,renderAvailableMask,renderSampledMask,nativeMainThreadMs,nativeRenderThreadMs,nativePlayerLoopMs,nativePresentWaitMs,nativeTargetFpsWaitMs,nativePresentMs,nativeRenderCommandsWaitMs,nativeRenderThreadWaitMs,nativeFrameStartWaitMs,nativeProcessCommandsMs,cpuPhaseEnabled,cpuPhaseFrame,cpuPhaseAvailableMask,cpuPhaseSampledMask,cpuInputUpdateMs,cpuNewInputUpdateMs,cpuLegacyInputUpdateMs,cpuPlayerConnectionMs,cpuPollPlayerConnectionMs,cpuProfilerMemoryMs,cpuBehaviourUpdateMs,cpuBehaviourLateUpdateMs,cpuDelayedTasksMs,cpuFinishFrameRenderingMs,cpuUpdateAllRenderersMs,mainAllocatedBytesAvailable,mainAllocatedBytesSampled,mainAllocatedBytes,mainAllocationStartFrame,mainAllocationEndFrame,mainAllocationStartTicks,mainAllocationEndTicks,cpuUrpRenderMs,cpuUrpMainCameraMs,cpuRenderGraphCompileMs,cpuRenderGraphExecuteMs,cpuRenderContextSubmitMs,engineUpdateCalls,engineUpdateMs,engineUpdateMaxMs,engineUpdateMaxStartSeconds,engineUpdateMaxContext,eliteDrumHitCalls,eliteDrumHitMs,eliteDrumHitMaxMs,eliteDrumHitMaxStartSeconds,eliteDrumHitMaxContext,classicDrumHitCalls,classicDrumHitMs,classicDrumHitMaxMs,classicDrumHitMaxStartSeconds,classicDrumHitMaxContext,drumHitBookkeepingCalls,drumHitBookkeepingMs,drumHitBookkeepingMaxMs,drumHitBookkeepingMaxStartSeconds,drumHitBookkeepingMaxContext,drumHitGemReturnCalls,drumHitGemReturnMs,drumHitGemReturnMaxMs,drumHitGemReturnMaxStartSeconds,drumHitGemReturnMaxContext,eliteHitFeedbackCalls,eliteHitFeedbackMs,eliteHitFeedbackMaxMs,eliteHitFeedbackMaxStartSeconds,eliteHitFeedbackMaxContext,classicHitFeedbackCalls,classicHitFeedbackMs,classicHitFeedbackMaxMs,classicHitFeedbackMaxStartSeconds,classicHitFeedbackMaxContext,fretHitAnimatorCalls,fretHitAnimatorMs,fretHitAnimatorMaxMs,fretHitAnimatorMaxStartSeconds,fretHitAnimatorMaxContext,fretHitColorCalls,fretHitColorMs,fretHitColorMaxMs,fretHitColorMaxStartSeconds,fretHitColorMaxContext,fretHitEffectCalls,fretHitEffectMs,fretHitEffectMaxMs,fretHitEffectMaxStartSeconds,fretHitEffectMaxContext,hitParticlePlayCalls,hitParticlePlayMs,hitParticlePlayMaxMs,hitParticlePlayMaxStartSeconds,hitParticlePlayMaxContext,hitLightPlayCalls,hitLightPlayMs,hitLightPlayMaxMs,hitLightPlayMaxStartSeconds,hitLightPlayMaxContext," +
                    "gameManagerUpdateCalls,gameManagerUpdateMs,gameManagerUpdateMaxMs,gameManagerUpdateMaxStartSeconds,gameManagerUpdateMaxContext,songRunnerUpdateCalls,songRunnerUpdateMs,songRunnerUpdateMaxMs,songRunnerUpdateMaxStartSeconds,songRunnerUpdateMaxContext,audioSynchronizeCalls,audioSynchronizeMs,audioSynchronizeMaxMs,audioSynchronizeMaxStartSeconds,audioSynchronizeMaxContext,audioClockQueryCalls,audioClockQueryMs,audioClockQueryMaxMs,audioClockQueryMaxStartSeconds,audioClockQueryMaxContext,audioClockCorrectionCalls,audioClockCorrectionMs,audioClockCorrectionMaxMs,audioClockCorrectionMaxStartSeconds,audioClockCorrectionMaxContext,gameplaySpeedUpdateCalls,gameplaySpeedUpdateMs,gameplaySpeedUpdateMaxMs,gameplaySpeedUpdateMaxStartSeconds,gameplaySpeedUpdateMaxContext,gameplayBeatEventsCalls,gameplayBeatEventsMs,gameplayBeatEventsMaxMs,gameplayBeatEventsMaxStartSeconds,gameplayBeatEventsMaxContext,gameplayCrowdEventsCalls,gameplayCrowdEventsMs,gameplayCrowdEventsMaxMs,gameplayCrowdEventsMaxStartSeconds,gameplayCrowdEventsMaxContext,gameplayPlayerUpdateCalls,gameplayPlayerUpdateMs,gameplayPlayerUpdateMaxMs,gameplayPlayerUpdateMaxStartSeconds,gameplayPlayerUpdateMaxContext,gameplayScoreUpdateCalls,gameplayScoreUpdateMs,gameplayScoreUpdateMaxMs,gameplayScoreUpdateMaxStartSeconds,gameplayScoreUpdateMaxContext,gameplaySongEndCalls,gameplaySongEndMs,gameplaySongEndMaxMs,gameplaySongEndMaxStartSeconds,gameplaySongEndMaxContext," +
                    "audioClockMixerLockCalls,audioClockMixerLockMs,audioClockMixerLockMaxMs,audioClockMixerLockMaxStartSeconds,audioClockMixerLockMaxContext," +
                    "audioClockNativeQueryCalls,audioClockNativeQueryMs,audioClockNativeQueryMaxMs,audioClockNativeQueryMaxStartSeconds,audioClockNativeQueryMaxContext," +
                    "audioClockBassConvertCalls,audioClockBassConvertMs,audioClockBassConvertMaxMs,audioClockBassConvertMaxStartSeconds,audioClockBassConvertMaxContext," +
                    "audioClockNativeSamples,audioClockNativeAttempts,audioClockCallbackYields,audioClockInvalidatedAttempts,audioClockSourceLockMs,audioClockCallbackBusyMs,audioClockInvalidatedMs,audioClockBassDecodePositionMs,audioClockBassHeardPositionMs");
                var row = new StringBuilder(512);
                int sinceFlush = 0;
                foreach (var s in Samples())
                {
                    row.Clear();
                    row.Append("10,");
                    Add(row, s.Frame); Add(row, s.Time); Add(row, s.WallMs); Add(row, s.UnityMs);
                    Text(row, s.Scene); Add(row, s.Focused); Add(row, s.Quality); Add(row, s.RequestedVSync);
                    Add(row, s.RequestedFps); Add(row, s.BackgroundFps); Add(row, s.VSync); Add(row, s.TargetFps);
                    Add(row, s.Width); Add(row, s.Height); Add(row, s.RefreshHz); Text(row, s.GraphicsApi); Add(row, s.BudgetMs);
                    Add(row, s.TimingEnabled); Add(row, s.HasTiming); Add(row, s.FreshTiming);
                    Optional(row, s.HasTiming, s.TimingTimestamp);
                    Optional(row, s.HasTiming, s.CpuMs); Optional(row, s.HasTiming, s.MainMs);
                    Optional(row, s.HasTiming, s.RenderMs); Optional(row, s.HasTiming, s.PresentWaitMs);
                    Optional(row, s.HasGpu, s.GpuMs);
                    Add(row, s.GC0); Add(row, s.GC1); Add(row, s.GC2);
                    Optional(row, s.HasGameplay, s.VisualTime); Optional(row, s.HasGameplay, s.VisualDelta);
                    Optional(row, s.HasGameplay, s.SongSpeed);
                    Add(row, s.PacingChanged);
                    Add(row, DroppedRows);
                    Add(row, s.IntervalStartFrame);
                    Add(row, s.Targeted.Enabled); Add(row, s.TargetedChanged); Add(row, s.Targeted.Generation);
                    AppendTargeted(row, s.Targeted);
                    row.Append(',');
                    AppendGc(row, s.GcDiagnostics);
                    row.Append(',');
                    AppendRender(row, s);
                    row.Append(',');
                    AppendCpuPhases(row, s.CpuPhases);
                    row.Append(',');
                    AppendHitTimings(row, s.Targeted);
                    row.Append(',');
                    AppendGameplayUpdateTimings(row, s.Targeted);
                    row.Append(',');
                    AppendAudioClockTimings(row, s.Targeted);
                    writer.WriteLine(row);
                    if (++sinceFlush >= 120) { writer.Flush(); sinceFlush = 0; }
                }
                writer.WriteLine($"# capture complete; droppedRows={DroppedRows}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                _error = e.Message;
            }
        }

        private static void AppendCpuPhases(StringBuilder row, CpuPhaseDiagnosticSample s)
        {
            Add(row, s.Enabled);
            Optional(row, s.Enabled, s.Frame);
            Optional(row, s.Enabled, s.AvailableMask);
            Optional(row, s.Enabled, s.SampledMask);
            Optional(row, s.Enabled && (s.SampledMask & 1) != 0, s.InputUpdateNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 2) != 0, s.NewInputUpdateNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 4) != 0, s.LegacyInputUpdateNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 8) != 0, s.PlayerConnectionNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 16) != 0, s.PollPlayerConnectionNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 32) != 0, s.ProfilerMemoryNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 64) != 0, s.BehaviourUpdateNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 128) != 0, s.BehaviourLateUpdateNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 256) != 0, s.DelayedTasksNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 512) != 0, s.FinishFrameRenderingNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 1024) != 0, s.UpdateAllRenderersNanoseconds * 1e-6);
            Add(row, s.MainAllocatedBytesAvailable);
            Add(row, s.MainAllocatedBytesSampled);
            bool allocated = s.Enabled && s.MainAllocatedBytesSampled;
            Optional(row, allocated, s.MainAllocatedBytes);
            Optional(row, allocated, s.AllocationStartFrame);
            Optional(row, allocated, s.AllocationEndFrame);
            Optional(row, allocated, s.AllocationStartTicks);
            Optional(row, allocated, s.AllocationEndTicks);
            Optional(row, s.Enabled && (s.SampledMask & (1 << 11)) != 0, s.UrpRenderNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & (1 << 12)) != 0, s.UrpMainCameraNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & (1 << 13)) != 0, s.RenderGraphCompileNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & (1 << 14)) != 0, s.RenderGraphExecuteNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & (1 << 15)) != 0, s.RenderContextSubmitNanoseconds * 1e-6);
            row.Length--;
        }

        private static void AppendHitTimings(StringBuilder row, TargetedPerformanceSnapshot s)
        {
            ContextTiming(row, s.Enabled, s.EngineUpdate);
            ContextTiming(row, s.Enabled, s.EliteDrumHit);
            ContextTiming(row, s.Enabled, s.ClassicDrumHit);
            ContextTiming(row, s.Enabled, s.DrumHitBookkeeping);
            ContextTiming(row, s.Enabled, s.DrumHitGemReturn);
            ContextTiming(row, s.Enabled, s.EliteHitFeedback);
            ContextTiming(row, s.Enabled, s.ClassicHitFeedback);
            ContextTiming(row, s.Enabled, s.FretHitAnimator);
            ContextTiming(row, s.Enabled, s.FretHitColor);
            ContextTiming(row, s.Enabled, s.FretHitEffect);
            ContextTiming(row, s.Enabled, s.HitParticlePlay);
            ContextTiming(row, s.Enabled, s.HitLightPlay);
            row.Length--;
        }

        private static void AppendGameplayUpdateTimings(StringBuilder row, TargetedPerformanceSnapshot s)
        {
            ContextTiming(row, s.Enabled, s.GameManagerUpdate);
            ContextTiming(row, s.Enabled, s.SongRunnerUpdate);
            ContextTiming(row, s.Enabled, s.AudioSynchronize);
            ContextTiming(row, s.Enabled, s.AudioClockQuery);
            ContextTiming(row, s.Enabled, s.AudioClockCorrection);
            ContextTiming(row, s.Enabled, s.GameplaySpeedUpdate);
            ContextTiming(row, s.Enabled, s.GameplayBeatEvents);
            ContextTiming(row, s.Enabled, s.GameplayCrowdEvents);
            ContextTiming(row, s.Enabled, s.GameplayPlayerUpdate);
            ContextTiming(row, s.Enabled, s.GameplayScoreUpdate);
            ContextTiming(row, s.Enabled, s.GameplaySongEnd);
            row.Length--;
        }

        private static void AppendAudioClockTimings(StringBuilder row, TargetedPerformanceSnapshot s)
        {
            ContextTiming(row, s.Enabled, s.AudioClockMixerLock);
            ContextTiming(row, s.Enabled, s.AudioClockNativeQuery);
            ContextTiming(row, s.Enabled, s.AudioClockBassConvert);
            var native = s.AudioClockNative;
            Optional(row, s.Enabled, native.Samples);
            bool sampled = s.Enabled && native.Samples > 0;
            Optional(row, sampled, native.Attempts);
            Optional(row, sampled, native.CallbackYields);
            Optional(row, sampled, native.InvalidatedAttempts);
            Optional(row, sampled, native.SourceLockNs * 1e-6);
            Optional(row, sampled, native.CallbackBusyNs * 1e-6);
            Optional(row, sampled, native.InvalidatedNs * 1e-6);
            Optional(row, sampled, native.BassDecodePositionNs * 1e-6);
            Optional(row, sampled, native.BassHeardPositionNs * 1e-6);
            row.Length--;
        }

        private static void AppendRender(StringBuilder row, FrameDiagnosticSample frame)
        {
            Optional(row, frame.HasTiming && frame.CpuTimerFrequency > 0, frame.CpuTimerFrequency);
            Optional(row, frame.HasTiming && frame.FirstSubmitTimestamp > 0, frame.FirstSubmitTimestamp);
            Optional(row, frame.HasTiming && frame.PresentCalledTimestamp > 0, frame.PresentCalledTimestamp);
            Optional(row, frame.HasTiming && frame.FrameCompleteTimestamp > 0, frame.FrameCompleteTimestamp);
            var s = frame.RenderDiagnostics;
            Add(row, s.Enabled);
            Optional(row, s.Enabled, s.Frame);
            Optional(row, s.Enabled, s.AvailableMask);
            Optional(row, s.Enabled, s.SampledMask);
            Optional(row, s.Enabled && (s.SampledMask & 1) != 0, s.MainThreadNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 2) != 0, s.RenderThreadNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 4) != 0, s.PlayerLoopNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 8) != 0, s.PresentWaitNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 16) != 0, s.TargetFpsWaitNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 32) != 0, s.PresentNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 64) != 0, s.RenderCommandsWaitNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 128) != 0, s.RenderThreadWaitNanoseconds * 1e-6);
            Optional(row, s.Enabled && (s.SampledMask & 256) != 0, s.FrameStartWaitNanoseconds * 1e-6);
            if (s.Enabled && (s.SampledMask & 512) != 0)
                row.Append((s.ProcessCommandsNanoseconds * 1e-6).ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendGc(StringBuilder row, GcDiagnosticSample s)
        {
            Add(row, s.Enabled);
            Optional(row, s.Enabled, s.Frame);
            Optional(row, s.Enabled, s.AvailableMask);
            Optional(row, s.Enabled, s.SampledMask);
            Optional(row, s.Enabled && (s.SampledMask & 1) != 0, s.AllocatedBytes);
            Optional(row, s.Enabled && (s.SampledMask & 2) != 0, s.MainAllocationCalls);
            Optional(row, s.Enabled && (s.SampledMask & 4) != 0, s.UsedBytes);
            Optional(row, s.Enabled && (s.SampledMask & 8) != 0, s.ReservedBytes);
            Optional(row, s.Enabled && (s.SampledMask & 16) != 0, s.IncrementalNanoseconds * 1e-6);
            if (s.Enabled && (s.SampledMask & 32) != 0)
                row.Append((s.CollectNanoseconds * 1e-6).ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendTargeted(StringBuilder row, TargetedPerformanceSnapshot s)
        {
            Timing(row, s.Enabled, s.RollSchedule); Timing(row, s.Enabled, s.RollResolve);
            Timing(row, s.Enabled, s.LaneMapBuild); Timing(row, s.Enabled, s.HiHatActivation);
            Timing(row, s.Enabled, s.HiHatDiscovery); Timing(row, s.Enabled, s.PoolPrewarm);
            Timing(row, s.Enabled, s.PoolCreate);
            Optional(row, s.Enabled, s.ScheduleNotesExamined); Optional(row, s.Enabled, s.ResolveNotesExamined);
            Optional(row, s.Enabled, s.LaneLookups); Optional(row, s.Enabled, s.LaneMembers);
            Optional(row, s.Enabled, s.DeadlinesQueued);
            Optional(row, s.Enabled, s.Continuations);
            ContextTiming(row, s.Enabled, s.NoteSpawn);
            ContextTiming(row, s.Enabled, s.ElementEnable);
            ContextTiming(row, s.Enabled, s.ElementSetActive);
            ContextTiming(row, s.Enabled, s.ElementInitialize);
            ContextTiming(row, s.Enabled, s.ElementFirstVisualUpdate);
            ContextTiming(row, s.Enabled, s.NoteGroupInitialize);
            ContextTiming(row, s.Enabled, s.MaterialAccess);
            ContextTiming(row, s.Enabled, s.NoteGroupSetActive);
            ContextTiming(row, s.Enabled, s.PedalMeshBuild);
            ContextTiming(row, s.Enabled, s.GameplayAwake);
            ContextTiming(row, s.Enabled, s.GameplayManagerDiscovery);
            Optional(row, s.Enabled, s.FirstElementEnables);
            if (s.Enabled) row.Append(s.FirstNoteGroupInitializations.ToString(CultureInfo.InvariantCulture));
        }

        private static void ContextTiming(StringBuilder row, bool enabled, TargetedTiming value)
        {
            Timing(row, enabled, value);
            Optional(row, enabled && value.Calls > 0, value.MaxStartTicks / (double)Stopwatch.Frequency);
            Optional(row, enabled && value.Calls > 0, value.MaxContext);
        }

        private static void Timing(StringBuilder row, bool enabled, TargetedTiming value)
        {
            const double MILLISECONDS_PER_SECOND = 1000.0;
            Optional(row, enabled, value.Calls);
            Optional(row, enabled, value.ElapsedTicks * (MILLISECONDS_PER_SECOND / Stopwatch.Frequency));
            Optional(row, enabled, value.MaxTicks * (MILLISECONDS_PER_SECOND / Stopwatch.Frequency));
        }

        private static void Add<T>(StringBuilder row, T value) where T : IConvertible
            => row.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        private static void Optional<T>(StringBuilder row, bool present, T value) where T : IConvertible
        {
            if (present) row.Append(value.ToString(CultureInfo.InvariantCulture));
            row.Append(',');
        }
        private static void Text(StringBuilder row, string value)
            => row.Append('"').Append((value ?? string.Empty).Replace("\"", "\"\"")).Append("\",");
    }
}
