#pragma once

#include "AudioRingBuffer.h"
#include "yarg_audio.h"

#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

namespace yarg::audio {

class IAudioSource {
public:
    virtual ~IAudioSource() = default;
    virtual bool prepareThread() noexcept = 0;
    virtual int read(float* samples, std::size_t frames) noexcept = 0;
    virtual int lastError() const noexcept = 0;
    virtual std::int64_t position(std::uint32_t sourceHandle,
        std::uint32_t delayBytes) noexcept = 0;
};

struct SourcePositionSnapshot {
    std::int64_t heardPosition;
    std::int64_t decodePosition;
    std::uint32_t totalDelayFrames;
};

inline std::uint64_t positionElapsedNs(std::chrono::steady_clock::time_point start) noexcept {
    return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::nanoseconds>(
        std::chrono::steady_clock::now() - start).count());
}

class RenderAheadMixer {
public:
    static constexpr std::size_t RenderChunkFrames = 128;

    RenderAheadMixer(std::unique_ptr<IAudioSource> source, std::uint32_t sampleRate,
        std::uint32_t channels, std::uint32_t callbackFrames,
        std::uint32_t bufferMilliseconds);
    ~RenderAheadMixer();
    RenderAheadMixer(const RenderAheadMixer&) = delete;
    RenderAheadMixer& operator=(const RenderAheadMixer&) = delete;

    bool start();
    bool prefill(std::chrono::milliseconds timeout);
    void stop() noexcept;
    bool clear();
    std::size_t consume(float* samples, std::size_t frames) noexcept;
    std::int64_t bufferedSourcePosition(std::uint32_t sourceHandle,
        std::uint32_t endpointDelayFrames) noexcept;
    template <typename DelayProvider>
    std::int64_t bufferedSourcePositionAfterLock(std::uint32_t sourceHandle,
        DelayProvider delayProvider) noexcept {
        std::lock_guard lock(sourceMutex_);
        return bufferedSourcePositionLocked(sourceHandle, delayProvider());
    }
    template <typename DelayProvider>
    int sourcePositionSnapshotAfterLock(std::uint32_t sourceHandle,
        SourcePositionSnapshot& snapshot, DelayProvider delayProvider,
        yarg_read_ahead_position_timing* timing, bool nonBlocking) noexcept {
        const auto start = timing ? std::chrono::steady_clock::now() : std::chrono::steady_clock::time_point{};
        std::unique_lock lock(sourceMutex_, std::defer_lock);
        if (nonBlocking) {
            lock.try_lock();
        } else {
            lock.lock();
        }
        if (timing) timing->source_lock_ns += positionElapsedNs(start);
        if (!lock.owns_lock()) return YARG_AUDIO_ERROR_BUSY;
        return sourcePositionSnapshotLocked(sourceHandle, delayProvider(), snapshot, timing)
            ? YARG_AUDIO_OK : YARG_AUDIO_ERROR_BASS;
    }

    std::size_t queuedFrames() const noexcept { return ring_.available(); }
    std::size_t targetFrames() const noexcept { return targetFrames_; }
    bool failed() const noexcept { return failed_.load(std::memory_order_acquire); }
    int lastError() const noexcept { return lastError_.load(std::memory_order_relaxed); }
    std::uint64_t producedFrames() const noexcept { return producedFrames_.load(); }
    std::uint64_t maximumRenderNanoseconds() const noexcept { return maximumRenderNs_.load(); }
    std::uint64_t positionOutputFrame() const noexcept { return positionOutputFrame_.load(); }

private:
    std::int64_t bufferedSourcePositionLocked(std::uint32_t sourceHandle,
        std::uint32_t endpointDelayFrames) noexcept;
    bool sourcePositionSnapshotLocked(std::uint32_t sourceHandle,
        std::uint32_t endpointDelayFrames, SourcePositionSnapshot& snapshot,
        yarg_read_ahead_position_timing* timing = nullptr) noexcept;
    void run() noexcept;
    void updateMaximum(std::uint64_t nanoseconds) noexcept;

    std::unique_ptr<IAudioSource> source_;
    const std::uint32_t channels_;
    const std::size_t targetFrames_;
    const std::size_t lowWatermarkFrames_;
    AudioRingBuffer ring_;
    std::vector<float> scratch_;
    std::mutex mutex_;
    std::mutex sourceMutex_;
    std::condition_variable wake_;
    std::condition_variable prefilled_;
    std::thread worker_;
    bool running_ = false;
    std::atomic<bool> stopping_{false};
    std::atomic<bool> failed_{false};
    std::atomic<int> lastError_{0};
    std::atomic<std::uint64_t> producedFrames_{0};
    std::atomic<std::uint64_t> generationProducedFrames_{0};
    std::atomic<std::uint64_t> maximumRenderNs_{0};
    std::atomic<std::uint64_t> positionOutputFrame_{0};
};

} // namespace yarg::audio
