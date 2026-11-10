using System;
using System.IO;
using Flint3.Core.Wordle;
using Xunit;

namespace Flint3.Core.Tests
{
    public class WordleStateStoreTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "flint-wordle-" + Guid.NewGuid().ToString("n"));

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }

        [Fact]
        public void SaveAndLoad_往返一致()
        {
            WordleStateStore store = new(Path.Combine(_directory, "wordle_state.json"));
            WordleState state = new()
            {
                Date = "2026-11-10",
                Guesses = ["apple", "brick"],
            };

            store.Save(state);
            WordleState? loaded = store.Load();

            Assert.NotNull(loaded);
            Assert.Equal("2026-11-10", loaded!.Date);
            Assert.Equal(new[] { "apple", "brick" }, loaded.Guesses);
        }

        [Fact]
        public void Load_文件不存在返回null()
        {
            WordleStateStore store = new(Path.Combine(_directory, "missing.json"));

            Assert.Null(store.Load());
        }

        [Fact]
        public void Load_损坏文件返回null()
        {
            string path = Path.Combine(_directory, "broken.json");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(path, "not json");

            WordleStateStore store = new(path);

            Assert.Null(store.Load());
        }
    }
}
