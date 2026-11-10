using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Flint3.Core.Wordle
{
    /// <summary>
    /// 当日 Wordle 的进度，按日期隔离，跨天自动失效
    /// </summary>
    public sealed class WordleState
    {
        public string Date { get; set; } = string.Empty;

        public List<string> Guesses { get; set; } = [];
    }

    /// <summary>
    /// Wordle 进度落盘，沿用非打包应用的用户数据目录
    /// </summary>
    public sealed class WordleStateStore
    {
        private readonly string _path;

        public WordleStateStore(string? path = null)
        {
            _path = path ?? DefaultPath;
        }

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Flint3",
            "FlintData",
            "wordle_state.json");

        public WordleState? Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                using FileStream stream = File.OpenRead(_path);
                return JsonSerializer.Deserialize(stream, Serialization.FlintJsonContext.Default.WordleState);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"读取 Wordle 进度失败: {ex}");
                return null;
            }
        }

        public void Save(WordleState state)
        {
            try
            {
                string? directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temp = _path + ".tmp";
                using (FileStream stream = File.Create(temp))
                {
                    JsonSerializer.Serialize(stream, state, Serialization.FlintJsonContext.Default.WordleState);
                }

                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"保存 Wordle 进度失败: {ex}");
            }
        }
    }
}
