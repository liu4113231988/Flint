using System.Threading;
using Flint3.Core.Query;
using Xunit;

namespace Flint3.Core.Tests
{
    public class SearchCoordinatorTests
    {
        [Fact]
        public void BeginRequest_新请求取消旧请求()
        {
            using var coordinator = new SearchCoordinator();

            var first = coordinator.BeginRequest();
            var second = coordinator.BeginRequest();

            Assert.True(first.Token.IsCancellationRequested);
            Assert.False(second.Token.IsCancellationRequested);
            Assert.True(second.Sequence > first.Sequence);
        }

        [Fact]
        public void IsCurrent_只有最新序号可写回()
        {
            using var coordinator = new SearchCoordinator();

            var first = coordinator.BeginRequest();
            Assert.True(coordinator.IsCurrent(first.Sequence));

            var second = coordinator.BeginRequest();

            Assert.False(coordinator.IsCurrent(first.Sequence));
            Assert.True(coordinator.IsCurrent(second.Sequence));
        }

        [Fact]
        public void Cancel_取消当前请求并使序号失效()
        {
            using var coordinator = new SearchCoordinator();
            var request = coordinator.BeginRequest();

            coordinator.Cancel();

            Assert.True(request.Token.IsCancellationRequested);
            Assert.False(coordinator.IsCurrent(request.Sequence));
        }

        [Fact]
        public void BeginRequest_返回递增序号()
        {
            using var coordinator = new SearchCoordinator();

            var first = coordinator.BeginRequest();
            var second = coordinator.BeginRequest();
            var third = coordinator.BeginRequest();

            Assert.Equal(first.Sequence + 1, second.Sequence);
            Assert.Equal(second.Sequence + 1, third.Sequence);
        }
    }
}
