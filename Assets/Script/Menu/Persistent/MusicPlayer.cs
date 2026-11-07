// pattern: Imperative Shell
using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Audio;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Settings;
using YARG.Song;

namespace YARG.Menu.Persistent
{
    public class MusicPlayer : MonoBehaviour
    {
        private static SongEntry _nowPlaying = null;
        public static SongEntry NowPlaying => _nowPlaying;

        private StemMixer _mixer;
        private SongEntry _lastPlayed;
        private Action _songEnded;
        private CancellationTokenSource _loadCancellation;
        private Task _loadChain = Task.CompletedTask;

        [SerializeField]
        private Image _playPauseButton;
        [SerializeField]
        private TextMeshProUGUI _songText;
        [SerializeField]
        private TextMeshProUGUI _artistText;

        [Space]
        [SerializeField]
        private Sprite _playSprite;
        [SerializeField]
        private Sprite _pauseSprite;

        private void OnEnable()
        {
            _songText.text = _artistText.text = string.Empty;
            QueueLoad(waitForLoadingScreen: true);
        }

        private void OnDisable()
        {
            _loadCancellation?.Cancel();
            StopPlayback();
            StemSettings.ApplySettings = SettingsManager.Settings.ApplyVolumesInMusicLibrary.Value;
        }

        public Task StopAndDrainAsync()
        {
            gameObject.SetActive(false);
            // OnDisable stops playback synchronously. The chain still owns any
            // decoder already running and will dispose its result before completing.
            return _loadChain;
        }

        public void NextSong() => QueueLoad(waitForLoadingScreen: false);

        private void QueueLoad(bool waitForLoadingScreen)
        {
            if (!isActiveAndEnabled) return;

            _loadCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _loadCancellation = cancellation;
            _loadChain = LoadNextSongAsync(_loadChain, cancellation, waitForLoadingScreen);
        }

        private async Task LoadNextSongAsync(Task previous, CancellationTokenSource cancellation,
            bool waitForLoadingScreen)
        {
            var token = cancellation.Token;
            try
            {
                // Serialize native loads so the scene transition can drain every
                // request, even when the user pressed Next several times.
                await previous;
                token.ThrowIfCancellationRequested();
                if (waitForLoadingScreen)
                    await UniTask.WaitUntil(() => !LoadingScreen.IsActive, cancellationToken: token);

                if (!isActiveAndEnabled || token.IsCancellationRequested) return;
                if (SongContainer.Count == 0)
                {
                    gameObject.SetActive(false);
                    return;
                }

                var settings = SettingsManager.Settings;
                StemSettings.ApplySettings = settings.ApplyVolumesInMusicPlayer.Value;
                var lastSong = _lastPlayed;
                const int MAX_TRIES = 20;
                for (int tries = 0; tries < MAX_TRIES; tries++)
                {
                    var entry = SongContainer.GetRandomSong();
                    if (entry == lastSong) continue;
                    lastSong = entry;

                    float volume = settings.MusicPlayerVolume.Value;
                    bool censor = settings.CensorMatureContent.Value;
                    var mixer = await Task.Run(() => entry.LoadAudio(1f, volume, censor, SongStem.Crowd), token);
                    if (token.IsCancellationRequested || !isActiveAndEnabled)
                    {
                        mixer?.Dispose();
                        return;
                    }
                    if (mixer == null) continue;

                    StopPlayback();
                    _mixer = mixer;
                    _nowPlaying = entry;
                    _lastPlayed = entry;
                    _songEnded = () => OnSongEnded(mixer);
                    mixer.SongEnd += _songEnded;
                    mixer.Play();
                    _songText.text = entry.Name;
                    _artistText.text = entry.Artist;
                    _playPauseButton.sprite = _pauseSprite;
                    return;
                }
                if (_mixer == null) _lastPlayed = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // A hidden menu or a newer request superseded this one.
            }
            catch (Exception exception)
            {
                YargLogger.LogException(exception, "Failed to load menu music.");
            }
            finally
            {
                if (ReferenceEquals(_loadCancellation, cancellation))
                    _loadCancellation = null;
                cancellation.Dispose();
            }
        }

        private void OnSongEnded(StemMixer mixer)
        {
            // The audio backend queues this callback to the Unity thread. A queued
            // callback may belong to a mixer that has already been disposed.
            if (!ReferenceEquals(_mixer, mixer)) return;

            StopPlayback();
            if (_loadCancellation == null) NextSong();
        }

        private void StopPlayback()
        {
            var mixer = _mixer;
            if (mixer == null) return;

            _mixer = null;
            _nowPlaying = null;
            mixer.SongEnd -= _songEnded;
            _songEnded = null;
            mixer.Dispose();
        }

        public void UpdateVolume(double volume) => _mixer?.SetVolume(volume);

        public void TogglePlay()
        {
            if (_mixer == null) return;

            if (!_mixer.IsPaused)
            {
                _mixer.Pause();
                _playPauseButton.sprite = _playSprite;
            }
            else
            {
                _mixer.Play();
                _playPauseButton.sprite = _pauseSprite;
            }
        }
    }
}
