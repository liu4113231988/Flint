using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flint3.Core.Query;
using Flint3.Dict;

namespace Flint3.Data.Mdx
{
    /// <summary>
    /// 管理已导入的 mdx 词库：文件副本、sidecar 索引与 dictionarySources.json
    /// </summary>
    public sealed class MdxDictionaryStore
    {
        private const string SettingsFileName = "dictionarySources.json";

        private readonly string _baseDirectory;

        public MdxDictionaryStore(string? baseDirectory = null)
        {
            _baseDirectory = baseDirectory ?? DefaultBaseDirectory;
        }

        public static string DefaultBaseDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Flint3", "FlintData");

        public string DictionariesDirectory => Path.Combine(_baseDirectory, "Dicts");

        private string SettingsPath => Path.Combine(_baseDirectory, SettingsFileName);

        public MdxDictionarySettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    return new MdxDictionarySettings();
                }

                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize(json, MdxJsonContext.Default.MdxDictionarySettings) ?? new MdxDictionarySettings();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                return new MdxDictionarySettings();
            }
        }

        public string GetDictionaryPath(MdxDictionaryInfo info)
        {
            return Path.Combine(DictionariesDirectory, info.FileName);
        }

        public string GetIndexPath(MdxDictionaryInfo info)
        {
            return Path.Combine(DictionariesDirectory, info.IndexFileName);
        }

        /// <summary>
        /// 复制词库并建立索引；同一文件重复导入时直接复用已有记录
        /// </summary>
        public async Task<MdxDictionaryInfo> ImportAsync(
            string mdxPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string sourcePath = Path.GetFullPath(mdxPath);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("找不到要导入的 mdx 文件", sourcePath);
            }

            string id = await ComputeIdAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            MdxDictionarySettings settings = Load();
            MdxDictionaryInfo? existing = settings.Dictionaries.Find(item => item.Id == id && File.Exists(GetIndexPath(item)));
            if (existing is not null)
            {
                return existing;
            }

            string title;
            using (MdxReader probe = MdxReader.Open(sourcePath))
            {
                title = string.IsNullOrWhiteSpace(probe.Header.Title) ? Path.GetFileNameWithoutExtension(sourcePath) : probe.Header.Title;
            }

            Directory.CreateDirectory(DictionariesDirectory);
            string fileName = id + ".mdx";
            string indexFileName = id + ".db";
            string targetPath = Path.Combine(DictionariesDirectory, fileName);
            string indexPath = Path.Combine(DictionariesDirectory, indexFileName);

            try
            {
                if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, overwrite: true);
                }

                long entryCount = await Task.Run(
                    () => MdxIndexDatabase.Build(targetPath, indexPath, progress, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                MdxDictionaryInfo info = new()
                {
                    Id = id,
                    Name = title,
                    FileName = fileName,
                    IndexFileName = indexFileName,
                    EntryCount = entryCount,
                    Enabled = true,
                };

                settings.Dictionaries.RemoveAll(item => item.Id == id);
                settings.Dictionaries.Add(info);
                Save(settings);
                return info;
            }
            catch
            {
                TryDelete(targetPath);
                TryDelete(indexPath);
                throw;
            }
        }

        public void Remove(MdxDictionaryInfo info)
        {
            TryDelete(GetDictionaryPath(info));
            TryDelete(GetIndexPath(info));

            MdxDictionarySettings settings = Load();
            settings.Dictionaries.RemoveAll(item => item.Id == info.Id);
            Save(settings);
        }

        public void SetEnabled(MdxDictionaryInfo info, bool enabled)
        {
            MdxDictionarySettings settings = Load();
            MdxDictionaryInfo? target = settings.Dictionaries.Find(item => item.Id == info.Id);
            if (target is not null)
            {
                target.Enabled = enabled;
                Save(settings);
            }
        }

        /// <summary>
        /// 为所有已启用且文件完整的词库创建查询源
        /// </summary>
        public IReadOnlyList<IWordSource> CreateSources()
        {
            List<IWordSource> sources = [];
            foreach (MdxDictionaryInfo info in Load().Dictionaries)
            {
                if (!info.Enabled)
                {
                    continue;
                }

                string mdxPath = GetDictionaryPath(info);
                string indexPath = GetIndexPath(info);
                if (File.Exists(mdxPath) && File.Exists(indexPath))
                {
                    sources.Add(new MdxSource(info.Name, mdxPath, indexPath));
                }
            }

            return sources;
        }

        private void Save(MdxDictionarySettings settings)
        {
            Directory.CreateDirectory(_baseDirectory);
            string json = JsonSerializer.Serialize(settings, MdxJsonContext.Default.MdxDictionarySettings);
            string tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
        }

        private static async Task<string> ComputeIdAsync(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            byte[] hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
        }
    }
}
