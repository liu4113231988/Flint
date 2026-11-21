namespace Flint3.Core.Input
{
    /// <summary>
    /// 选词手势判定：拖动超过阈值或完成双击，视为一次文本选择。
    /// 纯状态机，无原生依赖；仅在钩子线程单线程调用。
    /// </summary>
    public sealed class SelectionGestureDetector
    {
        private const uint WmMouseMove = 0x0200;
        private const uint WmLButtonDown = 0x0201;
        private const uint WmLButtonUp = 0x0202;

        // 拖动距离阈值（像素平方比较）：过滤单击与微小抖动，只有真正划选才触发
        private const int DragThresholdPx = 6;

        private bool _leftDown;
        private bool _dragged;
        private int _downX;
        private int _downY;
        private bool _doubleClick;
        private long _downTime;
        private long? _lastClick;
        private int _lastClickX;
        private int _lastClickY;
        private readonly uint _doubleClickTime;

        public SelectionGestureDetector(uint doubleClickTime = 500)
        {
            _doubleClickTime = doubleClickTime;
        }

        /// <summary>
        /// 喂入一条鼠标消息；完成拖选或双击时返回 true。
        /// </summary>
        public bool Process(uint message, int x, int y, long timestamp = 0)
        {
            switch (message)
            {
                case WmLButtonDown:
                    _leftDown = true;
                    _dragged = false;
                    _doubleClick = _lastClick is long previous && timestamp >= previous
                        && timestamp - previous <= _doubleClickTime
                        && System.Math.Abs(x - _lastClickX) < DragThresholdPx
                        && System.Math.Abs(y - _lastClickY) < DragThresholdPx;
                    _downTime = timestamp;
                    _downX = x;
                    _downY = y;
                    return false;

                case WmMouseMove:
                    if (_leftDown && !_dragged)
                    {
                        _dragged = HasDragged(x, y);
                    }

                    return false;

                case WmLButtonUp:
                    if (!_leftDown)
                    {
                        return false;
                    }

                    bool gesture = _dragged || _doubleClick || HasDragged(x, y);
                    _lastClick = gesture ? null : _downTime;
                    _lastClickX = _downX;
                    _lastClickY = _downY;
                    _leftDown = false;
                    _dragged = false;
                    return gesture;

                default:
                    return false;
            }
        }

        private bool HasDragged(int x, int y)
        {
            long dx = (long)x - _downX;
            long dy = (long)y - _downY;
            return System.Math.Abs(dx) >= DragThresholdPx || System.Math.Abs(dy) >= DragThresholdPx
                || dx * dx + dy * dy >= DragThresholdPx * DragThresholdPx;
        }
    }
}
