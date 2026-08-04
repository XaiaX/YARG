using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YARG.Core.Input;
using YARG.Input;
using YARG.Menu.Persistent;
using YARG.Player;

namespace YARG.Menu.Navigation
{
    public readonly struct NavigationContext
    {
        /// <summary>
        /// The type of navigation.
        /// </summary>
        public readonly MenuAction Action;

        /// <summary>
        /// The <see cref="YargPlayer"/> that this event was invoked from.
        /// </summary>
        public readonly YargPlayer Player;

        /// <summary>
        /// Whether or not this action is a repeat.
        /// </summary>
        public readonly bool IsRepeat;
        public readonly MenuInputSource Source;

        public NavigationContext(MenuAction action, YargPlayer player,
            MenuInputSource source = MenuInputSource.Unknown, bool repeat = false)
        {
            Action = action;
            Player = player;
            Source = source;
            IsRepeat = repeat;
        }

        public bool IsSameAs(NavigationContext other)
        {
            return other.Action == Action && other.Player == Player && other.Source == Source;
        }

        public NavigationContext AsRepeat()
        {
            return new NavigationContext(Action, Player, Source, true);
        }
    }

    [DefaultExecutionOrder(-10)]
    public class Navigator : MonoSingleton<Navigator>
    {
        private const float INPUT_REPEAT_TIME = 0.035f;
        private const float INPUT_REPEAT_COOLDOWN = 0.5f;

        private static readonly HashSet<MenuAction> RepeatActions = new()
        {
            MenuAction.Up,
            MenuAction.Down,
            MenuAction.Left,
            MenuAction.Right,
        };

        /// <summary>
        /// Tracks a held directional input so we can repeat it.
        /// </summary>
        private class RepeatContext
        {
            public readonly NavigationContext Context;
            public float Timer;

            public RepeatContext(NavigationContext context)
            {
                Context = context;
                Timer = INPUT_REPEAT_COOLDOWN;
            }
        }

        /// <summary>
        /// Tracks a held action
        /// </summary>
        private class NavigationHold
        {
            public readonly NavigationContext Context;
            public readonly HoldTracker Tracker;

            public NavigationHold(NavigationContext context, HoldTracker tracker)
            {
                Context = context;
                Tracker = tracker;
            }
        }

        public class HoldContext
        {
            public readonly NavigationContext Context;
            public float Timer;

            public HoldContext(NavigationContext context)
            {
                Context = context;
                Timer = INPUT_REPEAT_COOLDOWN;
            }
        }

        public bool MusicPlayerActive => HelpBar.Instance.MusicPlayer.isActiveAndEnabled;

        private bool _disableMenuInputs;
        private int _nextControllerLockToken;
        private readonly HashSet<int> _controllerLockTokens = new();

        public bool ControllerLockEnabled => _controllerLockTokens.Count > 0;

        public bool DisableMenuInputs
        {
            get => _disableMenuInputs;
            set => SetDisableMenuInputs(value);
        }

        public event Action<NavigationContext> NavigationEvent;

        private readonly List<RepeatContext> _repeatInputs = new();
        private readonly List<NavigationHold> _holdInputs = new();
        private readonly Stack<NavigationScheme> _schemeStack = new();

        private void SetDisableMenuInputs(bool value)
        {
            if (_disableMenuInputs == value) return;

            _disableMenuInputs = value;

            if (_disableMenuInputs)
            {
                ClearHeldInputs();
            }
        }

        private void ClearHeldInputs()
        {
            _repeatInputs.Clear();

            for (int i = _holdInputs.Count - 1; i >= 0; i--)
            {
                _holdInputs[i].Tracker.StopHolding();
                _holdInputs[i].Tracker.ClearEvents();
            }

            _holdInputs.Clear();
        }

        public System.IDisposable AcquireControllerLock()
        {
            int token = ++_nextControllerLockToken;
            bool wasEnabled = ControllerLockEnabled;
            _controllerLockTokens.Add(token);
            if (!wasEnabled)
            {
                ClearHeldInputs();
            }

            return new ControllerLockScope(this, token);
        }

        private void ReleaseControllerLock(int token)
        {
            _controllerLockTokens.Remove(token);
        }

        private sealed class ControllerLockScope : System.IDisposable
        {
            private Navigator _owner;
            private readonly int _token;

            public ControllerLockScope(Navigator owner, int token)
            {
                _owner = owner;
                _token = token;
            }

            public void Dispose()
            {
                if (_owner == null) return;
                _owner.ReleaseControllerLock(_token);
                _owner = null;
            }
        }

        private void Start()
        {
            InputManager.MenuInput += ProcessInput;
            UpdateHelpBar().Forget();
        }

        private void Update()
        {
            if (ShouldBlockInputs())
            {
                if (_repeatInputs.Count > 0 || _holdInputs.Count > 0)
                {
                    ClearHeldInputs();
                }
                return;
            }

            if (ControllerLockEnabled)
            {
                for (int i = _repeatInputs.Count - 1; i >= 0; i--)
                {
                    if (_repeatInputs[i].Context.Source == MenuInputSource.Controller)
                        _repeatInputs.RemoveAt(i);
                }

                for (int i = _holdInputs.Count - 1; i >= 0; i--)
                {
                    if (_holdInputs[i].Context.Source != MenuInputSource.Controller)
                        continue;

                    _holdInputs[i].Tracker.StopHolding();
                    _holdInputs[i].Tracker.ClearEvents();
                    _holdInputs.RemoveAt(i);
                }
            }

            foreach (var hold in _holdInputs)
            {
                hold.Tracker.Tick();
            }

            foreach (var repeat in _repeatInputs)
            {
                repeat.Timer -= Time.unscaledDeltaTime;

                if (repeat.Timer <= 0f)
                {
                    repeat.Timer = INPUT_REPEAT_TIME;
                    InvokeNavigationEvent(repeat.Context.AsRepeat());
                }
            }
        }

        private void ProcessInput(YargPlayer player, UnityEngine.InputSystem.InputDevice device,
            MenuInputSource source, ref GameInput input)
        {
            if (ShouldBlockInputs() || (ControllerLockEnabled && source == MenuInputSource.Controller))
                return;

            var action = (MenuAction) input.Action;

            // Swap up and down for lefty flip
            if (player?.Profile.LeftyFlip == true)
            {
                action = action switch
                {
                    MenuAction.Up    => MenuAction.Down,
                    MenuAction.Down  => MenuAction.Up,
                    MenuAction.Left  => MenuAction.Right,
                    MenuAction.Right => MenuAction.Left,
                    _                => action
                };
            }

            var context = new NavigationContext(action, player, source);

            if (input.Button)
            {
                StartNavigationHold(context);
            }
            else
            {
                EndNavigationHold(context);
            }
        }

        private void StartNavigationHold(NavigationContext context)
        {
            // Skip if the input is already being tracked as a hold
            if (_holdInputs.Any(i => i.Context.IsSameAs(context)))
            {
                return;
            }

            // Skip if the input is already being tracked as a repeat
            if (_repeatInputs.Any(i => i.Context.IsSameAs(context)))
            {
                return;
            }

            if (_schemeStack.Count > 0 &&
                _schemeStack.Peek().TryGetHoldSeconds(context.Action, out var holdSeconds))
            {
                var tracker = new HoldTracker(holdSeconds);
                var navHold = new NavigationHold(context, tracker);

                var ctx = context;
                tracker.OnClick += () => InvokeNavigationEvent(ctx);
                tracker.OnHoldComplete += () => InvokeHoldEvent(ctx);

                tracker.StartHolding();
                _holdInputs.Add(navHold);
                return;
            }

            InvokeNavigationEvent(context);

            if (RepeatActions.Contains(context.Action))
            {
                _repeatInputs.Add(new RepeatContext(context));
            }
        }

        private void EndNavigationHold(NavigationContext context)
        {
            for (int i = _holdInputs.Count - 1; i >= 0; i--)
            {
                if (!_holdInputs[i].Context.IsSameAs(context))
                {
                    continue;
                }

                _holdInputs[i].Tracker.StopHolding();
                _holdInputs[i].Tracker.ClearEvents();
                _holdInputs.RemoveAt(i);
            }

            // Remove matching repeat inputs
            for (int i = _repeatInputs.Count - 1; i >= 0; i--)
            {
                if (_repeatInputs[i].Context.IsSameAs(context))
                {
                    _repeatInputs.RemoveAt(i);
                }
            }

            InvokeHoldOffEvent(context);
        }

        public float GetHoldProgress(MenuAction action)
        {
            float progress = -1f;
            foreach (var hold in _holdInputs)
            {
                if (hold.Context.Action == action)
                {
                    progress = Mathf.Max(progress, hold.Tracker.HoldProgress);
                }
            }
            return progress;
        }

        private void InvokeNavigationEvent(NavigationContext ctx)
        {
            if (ShouldBlockContext(ctx))
            {
                return;
            }

            NavigationEvent?.Invoke(ctx);

            if (_schemeStack.Count > 0)
            {
                _schemeStack.Peek().InvokeFuncs(ctx);
            }
        }

        private void InvokeHoldOffEvent(NavigationContext ctx)
        {
            if (ShouldBlockContext(ctx))
            {
                return;
            }

            if (_schemeStack.Count > 0)
            {
                _schemeStack.Peek().InvokeHoldOffFuncs(ctx);
            }
        }

        private void InvokeHoldEvent(NavigationContext ctx)
        {
            if (ShouldBlockContext(ctx))
            {
                return;
            }

            if (_schemeStack.Count > 0)
            {
                _schemeStack.Peek().InvokeHoldFuncs(ctx);
            }
        }

        public void PushScheme(NavigationScheme scheme)
        {
            _schemeStack.Push(scheme);
            UpdateHelpBar().Forget();
        }

        public bool IsTopScheme(NavigationScheme scheme) =>
            scheme != null && _schemeStack.Count > 0 && ReferenceEquals(_schemeStack.Peek(), scheme);

        public void PopScheme()
        {
            var scheme = _schemeStack.Pop();
            scheme.PopCallback?.Invoke();
            UpdateHelpBar().Forget();
        }

        public void PopAllSchemes()
        {
            // Pop all one by one so we can call each callback (instead of clearing)
            while (_schemeStack.Count >= 1)
            {
                var scheme = _schemeStack.Pop();
                scheme.PopCallback?.Invoke();
            }

            UpdateHelpBar().Forget();
        }

        private async UniTask UpdateHelpBar()
        {
            // Wait one frame to update, in case another one gets pushed.
            // This prevents the music player from resetting across schemes.
            await UniTask.WaitForEndOfFrame(this);

            if (_schemeStack.Count <= 0)
            {
                HelpBar.Instance.Reset();
            }
            else
            {
                HelpBar.Instance.SetInfoFromScheme(_schemeStack.Peek());
            }
        }

        private bool ShouldBlockInputs()
        {
            return DisableMenuInputs || LoadingScreen.IsActive;
        }

        private bool ShouldBlockContext(NavigationContext context)
        {
            return ShouldBlockInputs() ||
                (ControllerLockEnabled && context.Source == MenuInputSource.Controller);
        }
    }
}
