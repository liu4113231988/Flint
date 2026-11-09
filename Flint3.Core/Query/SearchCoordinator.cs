using System;
using System.Threading;

namespace Flint3.Core.Query
{
    public readonly record struct SearchRequest(long Sequence, CancellationToken Token);

    /// <summary>
    /// 搜索请求的取消与序号管理：新请求会取消旧请求，只有最新序号允许写回结果
    /// </summary>
    public sealed class SearchCoordinator : IDisposable
    {
        private readonly object _gate = new();
        private CancellationTokenSource? _current;
        private long _sequence;

        public SearchRequest BeginRequest()
        {
            CancellationTokenSource cts = new();
            CancellationTokenSource? previous;
            long sequence;

            lock (_gate)
            {
                previous = _current;
                _current = cts;
                sequence = ++_sequence;
            }

            // 取消在锁外执行，避免取消回调在锁内等待造成互相阻塞
            previous?.Cancel();
            previous?.Dispose();

            return new SearchRequest(sequence, cts.Token);
        }

        /// <summary>
        /// 写回结果前校验，防止旧请求覆盖新结果
        /// </summary>
        public bool IsCurrent(long sequence)
        {
            lock (_gate)
            {
                return sequence == _sequence;
            }
        }

        public void Cancel()
        {
            CancellationTokenSource? current;
            lock (_gate)
            {
                current = _current;
                _current = null;
                _sequence++;
            }

            current?.Cancel();
            current?.Dispose();
        }

        public void Dispose() => Cancel();
    }
}
