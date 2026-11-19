namespace Flint3.Core.Input
{
    /// <summary>
    /// 选词手势判定：左键按下 → 拖动超过阈值 → 抬起，视为一次文本选择。
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

        /// <summary>
        /// 喂入一条鼠标消息；当完成一次"按下→拖动→抬起"手势时返回 true
        /// </summary>
        public bool Process(uint message, int x, int y)
        {
            switch (message)
            {
                case WmLButtonDown:
                    _leftDown = true;
                    _dragged = false;
                    _downX = x;
                    _downY = y;
                    return false;

                case WmMouseMove:
                    if (_leftDown && !_dragged)
                    {
                        int dx = x - _downX;
                        int dy = y - _downY;
                        if (dx * dx + dy * dy >= DragThresholdPx * DragThresholdPx)
                        {
                            _dragged = true;
                        }
                    }

                    return false;

                case WmLButtonUp:
                    bool gesture = _leftDown && _dragged;
                    _leftDown = false;
                    _dragged = false;
                    return gesture;

                default:
                    return false;
            }
        }
    }
}