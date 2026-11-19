using System;

namespace Flint3.Core.Input
{
    /// <summary>
    /// 自动划词监视器：全局鼠标钩子观察选词手势，命中后触发 <see cref="SelectionGesture"/>。
    /// 须在有消息循环的线程（UI 线程）上 <see cref="Start"/>；事件在该线程触发，订阅方不得阻塞。
    /// </summary>
    public sealed class AutoSelectionMonitor : IDisposable
    {
        private readonly MouseHook _hook;
        private readonly SelectionGestureDetector _detector = new();

        public AutoSelectionMonitor()
        {
            _hook = new MouseHook(OnMouseEvent);
        }

        /// <summary>
        /// 检测到一次选词手势（左键拖动后抬起）；在钩子线程触发
        /// </summary>
        public event Action? SelectionGesture;

        public bool IsInstalled => _hook.IsInstalled;

        /// <summary>
        /// 安装鼠标钩子开始监视；失败返回 false（降级为自动划词不可用）
        /// </summary>
        public bool Start() => _hook.Start();

        private void OnMouseEvent(MouseEvent ev)
        {
            if (ev.ExtraInfo == InputSimulator.InjectedKeyMarker)
            {
                return;
            }

            if (_detector.Process(ev.Message, ev.X, ev.Y))
            {
                SelectionGesture?.Invoke();
            }
        }

        public void Dispose() => _hook.Dispose();
    }
}