using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Flint3.Core.Serialization;

namespace Flint3.Core.Settings
{
    /// <summary>
    /// 免打包/无包标识时的本地 JSON 设置存储
    /// </summary>
    public sealed class JsonFileSettingsStore : ISettingsStore
    {
        private readonly object _gate = new();
        private readonly string _filePath;
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public JsonFileSettingsStore(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Flint3",
                "settings.json");

            Load();
        }

        public object? this[string key]
        {
            get
            {
                lock (_gate)
                {
                    return _values.TryGetValue(key, out string? value) ? value : null;
                }
            }
            set
            {
                lock (_gate)
                {
                    _values[key] = value?.ToString() ?? string.Empty;
                    Save();
                }
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return;
                }

                Dictionary<string, string>? loaded = JsonSerializer.Deserialize(
                    File.ReadAllText(_filePath),
                    FlintJsonContext.Default.DictionaryStringString);

                if (loaded is null)
                {
                    return;
                }

                foreach (KeyValuePair<string, string> pair in loaded)
                {
                    _values[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(_values, FlintJsonContext.Default.DictionaryStringString));
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }
    }
}
