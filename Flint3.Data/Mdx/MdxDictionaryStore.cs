using System;
using System.Collections.Concurrent;
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

        // 复制阶段占总进度的前 15%，其余留给索引构建
        private const double CopyProgressShare = 0.15;

        private readonly string _baseDirectory;

        private readonly ConcurrentDictionary<string, byte> _upgrading = new();

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
                    await CopyFileAsync(sourcePath, targetPath, progress, cancellationToken).ConfigureAwait(false);
                }

                IProgress<double>? indexProgress = progress is null
                    ? null
                    : new ScaledProgress(progress, CopyProgressShare, 1 - CopyProgressShare);

                long entryCount = await Task.Run(
                    () => MdxIndexDatabase.Build(targetPath, indexPath, indexProgress, cancellationToken),
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
        /// 为所有已启用且文件完整的词库创建查询源；旧结构版本的索引会在后台自动重建
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
                    ScheduleIndexUpgrade(info);
                }
            }

            return sources;
        }

        /// <summary>
        /// 用已有的 mdx 副本重建 sidecar 索引；失败时抛出，旧索引保持不动、查询仍可继续
        /// </summary>
        public Task RebuildIndexAsync(
            MdxDictionaryInfo info,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            string mdxPath = GetDictionaryPath(info);
            string indexPath = GetIndexPath(info);
            if (!File.Exists(mdxPath))
            {
                throw new FileNotFoundException("找不到词库副本，无法重建索引", mdxPath);
            }

            return Task.Run(() =>
            {
                MdxIndexDatabase.Build(mdxPath, indexPath, progress, cancellationToken);

                // 重建期间词库可能已被删除，避免留下无人引用的索引文件
                if (!Load().Dictionaries.Exists(item => item.Id == info.Id))
                {
                    TryDelete(indexPath);
                }
            }, cancellationToken);
        }

        private void ScheduleIndexUpgrade(MdxDictionaryInfo info)
        {
            if (MdxIndexDatabase.IsCurrent(GetIndexPath(info)) || !_upgrading.TryAdd(info.Id, 0))
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await RebuildIndexAsync(info).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 旧索引仍可查询，失败只影响体积，下次打开会再次尝试
                    Trace.WriteLine(ex);
                }
                finally
                {
                    _upgrading.TryRemove(info.Id, out _);
                }
            });
        }

        private void Save(MdxDictionarySettings settings)
        {
            Directory.CreateDirectory(_baseDirectory);
            string json = JsonSerializer.Serialize(settings, MdxJsonContext.Default.MdxDictionarySettings);
            string tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, SettingsPath, overwrite: true);
        }

        /// <summary>
        /// 分块异步复制，避免大文件在 UI 线程上同步拷贝时卡住，并按块响应取消
        /// </summary>
        private static async Task CopyFileAsync(
            string sourcePath,
            string targetPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            const int BufferSize = 1024 * 1024;
            await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            await using FileStream target = new(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

            long total = source.Length;
            long copied = 0;
            byte[] buffer = new byte[BufferSize];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                copied += read;
                progress?.Report(total > 0 ? CopyProgressShare * copied / total : 0);
            }
        }

        private static async Task<string> ComputeIdAsync(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            byte[] hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash)[..12].ToLowerInvariant();
        }

        /// <summary>
        /// 同步转发并做区间缩放，保持索引构建的 0~1 进度落在总进度的后 85%
        /// </summary>
        private sealed class ScaledProgress : IProgress<double>
        {
            private readonly IProgress<double> _inner;
            private readonly double _offset;
            private readonly double _scale;

            public ScaledProgress(IProgress<double> inner, double offset, double scale)
            {
                _inner = inner;
                _offset = offset;
                _scale = scale;
            }

            public void Report(double value) => _inner.Report(_offset + (_scale * value));
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
