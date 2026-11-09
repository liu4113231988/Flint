using System;
using Flint3.Core.Input;

namespace Flint3.Models
{
    public delegate void KeyEvent(int key);

    public delegate bool IsActive();

    public delegate bool FilterAccessibleKeyboardEvents(int key, UIntPtr extraInfo);

    public class HotkeySettingsControlHook : IDisposable
    {
        private const int WmKeyDown = 0x100;
        private const int WmKeyUp = 0x101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;

        private KeyboardHook _hook;
        private KeyEvent _keyDown;
        private KeyEvent _keyUp;
        private IsActive _isActive;
        private bool disposedValue;

        private FilterAccessibleKeyboardEvents _filterKeyboardEvent;

        public HotkeySettingsControlHook(KeyEvent keyDown, KeyEvent keyUp, IsActive isActive, FilterAccessibleKeyboardEvents filterAccessibleKeyboardEvents)
        {
            _keyDown = keyDown;
            _keyUp = keyUp;
            _isActive = isActive;
            _filterKeyboardEvent = filterAccessibleKeyboardEvents;
            _hook = new KeyboardHook(HotkeySettingsHookCallback, IsActive, FilterKeyboardEvents);
            _hook.Start();
        }

        private bool IsActive()
        {
            return _isActive();
        }

        private void HotkeySettingsHookCallback(KeyboardEvent ev)
        {
            switch (ev.Message)
            {
                case WmKeyDown:
                case WmSysKeyDown:
                    _keyDown(ev.Key);
                    break;
                case WmKeyUp:
                case WmSysKeyUp:
                    _keyUp(ev.Key);
                    break;
            }
        }

        private bool FilterKeyboardEvents(KeyboardEvent ev)
        {
            return _filterKeyboardEvent(ev.Key, ev.ExtraInfo);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // Dispose the KeyboardHook object to terminate the hook threads
                    _hook.Dispose();
                }

                disposedValue = true;
            }
        }

        public bool GetDisposedState() => disposedValue;

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
