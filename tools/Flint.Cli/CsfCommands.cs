using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flint3.Data;
using Flint3.Data.Csf;
using Flint3.Data.Models;
using Microsoft.Data.Sqlite;

namespace Flint.Cli
{
    /// <summary>
    /// CSF 构建/对拍/基准命令（临时验证用途，随工具一起发布）
    /// </summary>
    internal static class CsfCommands
    {
        public static int Build(string databasePath, string csfPath)
        {
            CsfBuildStats stats = CsfWriter.Build(databasePath, csfPath);
            Console.WriteLine("mode=build");
            Console.WriteLine($"db={databasePath}");
            Console.WriteLine($"csf={csfPath}");
            Console.WriteLine($"records={stats.RecordCount}");
            Console.WriteLine($"record_blocks={stats.RecordBlockCount} word_blocks={stats.WordIndexBlockCount} sw_blocks={stats.SwIndexBlockCount}");
            Console.WriteLine($"records_mb={stats.RecordBytes / 1024d / 1024d:F1}");
            Console.WriteLine($"word_index_mb={stats.WordIndexBytes / 1024d / 1024d:F1}");
            Console.WriteLine($"sw_index_mb={stats.SwIndexBytes / 1024d / 1024d:F1}");
            Console.WriteLine($"aux_mb={stats.AuxiliaryBytes / 1024d / 1024d:F1}");
            Console.WriteLine($"file_mb={stats.FileBytes / 1024d / 1024d:F2}");
            Console.WriteLine($"elapsed_s={stats.Elapsed.TotalSeconds:F1}");
            return 0;
        }

        public static async Task<int> VerifyAsync(string databasePath, string csfPath, bool deep)
        {
            int failures = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();

            using CsfReader reader = new(csfPath);
            Console.WriteLine($"csf_records={reader.RecordCount}");

            // 1. 词头索引：与 SQLite 的 order by word collate nocase 逐条比对键与 id
            failures += CompareIndex(databasePath, "select id, word from stardict order by word collate nocase",
                reader.ForEachWordIndexEntry, "word-index", true);

            // 2. sw 索引：与 order by sw, word collate nocase 逐条比对
            failures += CompareIndex(databasePath, "select id, sw from stardict order by sw, word collate nocase",
                reader.ForEachSwIndexEntry, "sw-index", false);

            // 3. 端到端查询对拍（走 StarDictDataAccess，覆盖记录解码与字段映射）
            List<(int Id, string Word)> samples = ReadSamples(databasePath, 5000);

            StarDictDataAccess.SetBackend(StarDictBackendKind.Sqlite);
            await StarDictDataAccess.InitializeDatabase(databasePath).ConfigureAwait(false);
            Dictionary<int, string> sqliteExact = [];
            foreach ((int id, string word) in samples)
            {
                List<StarDictWordItem> items = await StarDictDataAccess.QueryWord(word).ConfigureAwait(false);
                sqliteExact[id] = Format(items);
            }

            List<string> sqlitePrefix = [];
            foreach ((int _, string word) in samples.Take(1000))
            {
                string prefix = word.Length <= 1 ? word : word[..Math.Min(4, word.Length)];
                sqlitePrefix.Add(Format(await StarDictDataAccess.MatchWord(prefix, 10).ConfigureAwait(false)));
            }

            string[] patterns = ["app%", "%ing", "_a_e", "%tion", "a%", "%zz%", "pre%", "%ness"];
            List<string> sqlitePatterns = [];
            foreach (string pattern in patterns)
            {
                sqlitePatterns.Add(Format(await StarDictDataAccess.PatternWord(pattern, null, 20).ConfigureAwait(false)));
                sqlitePatterns.Add(Format(await StarDictDataAccess.PatternWord(pattern, 5, 20).ConfigureAwait(false)));
            }

            string[] tags = ["zk", "gk", "ky", "cet4", "cet6", "toefl", "ielts", "gre", "oxford"];
            Dictionary<string, string> sqliteGlossary = [];
            Dictionary<string, int> sqliteCounts = [];
            foreach (string tag in tags)
            {
                sqliteCounts[tag] = await StarDictDataAccess.GetExtraGlossaryWordsCount(tag).ConfigureAwait(false);
                sqliteGlossary[tag] = Format(await StarDictDataAccess.GetAllExtraGlossaryWords(tag, string.Empty, default).ConfigureAwait(false));
            }

            List<string> sqliteIncremental = [];
            foreach ((string tag, long startId, int limit) in new[] { ("zk", 0L, 50), ("gk", 500000L, 50), ("oxford", 1000000L, 30), ("cet4", 2000000L, 40) })
            {
                sqliteIncremental.Add(Format(await StarDictDataAccess.GetIncrementalExtraGlossaryWords(tag, startId, limit, string.Empty, default).ConfigureAwait(false)));
            }

            StarDictDataAccess.SetBackend(StarDictBackendKind.Csf);
            await StarDictDataAccess.InitializeDatabase(csfPath).ConfigureAwait(false);

            foreach ((int id, string word) in samples)
            {
                string actual = Format(await StarDictDataAccess.QueryWord(word).ConfigureAwait(false));
                if (actual != sqliteExact[id])
                {
                    failures += Report($"exact id={id} word={word}", sqliteExact[id], actual);
                }
            }

            for (int i = 0; i < 1000; i++)
            {
                (int _, string word) = samples[i];
                string prefix = word.Length <= 1 ? word : word[..Math.Min(4, word.Length)];
                string actual = Format(await StarDictDataAccess.MatchWord(prefix, 10).ConfigureAwait(false));
                if (actual != sqlitePrefix[i])
                {
                    failures += Report($"prefix '{prefix}'", sqlitePrefix[i], actual);
                }
            }

            for (int i = 0; i < patterns.Length; i++)
            {
                string actualNoLength = Format(await StarDictDataAccess.PatternWord(patterns[i], null, 20).ConfigureAwait(false));
                if (actualNoLength != sqlitePatterns[i * 2])
                {
                    failures += Report($"pattern '{patterns[i]}'", sqlitePatterns[i * 2], actualNoLength);
                }

                string actualLength = Format(await StarDictDataAccess.PatternWord(patterns[i], 5, 20).ConfigureAwait(false));
                if (actualLength != sqlitePatterns[i * 2 + 1])
                {
                    failures += Report($"pattern '{patterns[i]}' len=5", sqlitePatterns[i * 2 + 1], actualLength);
                }
            }

            foreach (string tag in tags)
            {
                int count = await StarDictDataAccess.GetExtraGlossaryWordsCount(tag).ConfigureAwait(false);
                if (count != sqliteCounts[tag])
                {
                    failures += Report($"count '{tag}'", sqliteCounts[tag].ToString(), count.ToString());
                }

                string list = Format(await StarDictDataAccess.GetAllExtraGlossaryWords(tag, string.Empty, default).ConfigureAwait(false));
                if (list != sqliteGlossary[tag])
                {
                    failures += Report($"glossary '{tag}'", sqliteGlossary[tag], list);
                }
            }

            for (int i = 0; i < sqliteIncremental.Count; i++)
            {
                (string tag, long startId, int limit) = new[] { ("zk", 0L, 50), ("gk", 500000L, 50), ("oxford", 1000000L, 30), ("cet4", 2000000L, 40) }[i];
                string actual = Format(await StarDictDataAccess.GetIncrementalExtraGlossaryWords(tag, startId, limit, string.Empty, default).ConfigureAwait(false));
                if (actual != sqliteIncremental[i])
                {
                    failures += Report($"incremental '{tag}' start={startId}", sqliteIncremental[i], actual);
                }
            }

            if (deep)
            {
                // 逐条记录字段对拍：抽样 2000 条，比较全部输出字段
                foreach ((int id, string word) in ReadSamples(databasePath, 2000))
                {
                    StarDictWordItem? sqliteItem = ReadSqliteItem(databasePath, id);
                    List<StarDictWordItem> items = await StarDictDataAccess.QueryWord(word).ConfigureAwait(false);
                    StarDictWordItem? csfItem = items.Count == 1 ? items[0] : null;
                    if (csfItem is null || sqliteItem is null || !ItemEquals(sqliteItem, csfItem))
                    {
                        failures++;
                        Console.WriteLine($"FAIL record id={id} word={word}");
                    }
                }
            }

            stopwatch.Stop();
            Console.WriteLine($"failures={failures}");
            Console.WriteLine($"elapsed_s={stopwatch.Elapsed.TotalSeconds:F1}");
            Console.WriteLine(failures == 0 ? "RESULT=PASS" : "RESULT=FAIL");
            return failures == 0 ? 0 : 1;
        }

        public static async Task<int> BenchAsync(string databasePath, string csfPath)
        {
            // 稀疏抽样在整个 id 区间上均匀取 500 条，避免只看低 id 段
            List<(int Id, string Word)> spread = ReadSamples(databasePath, 5000);
            List<(int Id, string Word)> samples = [.. spread.Where((_, index) => index % 10 == 0).Take(500)];
            List<string> prefixes = [.. samples.Take(300).Select(s => s.Word.Length <= 1 ? s.Word : s.Word[..Math.Min(4, s.Word.Length)])];
            string[] patterns = ["app%", "%ing", "_a_e"];

            // 用 FLINT_BENCH_BACKEND 限定单个后端，便于在独立进程里测峰值内存
            string? only = Environment.GetEnvironmentVariable("FLINT_BENCH_BACKEND")?.Trim().ToLowerInvariant();
            StarDictBackendKind[] backends = only == "sqlite"
                ? [StarDictBackendKind.Sqlite]
                : only == "csf" ? [StarDictBackendKind.Csf] : [StarDictBackendKind.Sqlite, StarDictBackendKind.Csf];

            foreach (StarDictBackendKind kind in backends)
            {
                string path = kind == StarDictBackendKind.Sqlite ? databasePath : csfPath;
                StarDictDataAccess.SetBackend(kind);
                Stopwatch open = Stopwatch.StartNew();
                await StarDictDataAccess.InitializeDatabase(path).ConfigureAwait(false);
                _ = await StarDictDataAccess.QueryWord(samples[0].Word).ConfigureAwait(false);
                open.Stop();

                List<double> exact = [];
                foreach ((int _, string word) in samples)
                {
                    exact.Add(await Timed(() => StarDictDataAccess.QueryWord(word)).ConfigureAwait(false));
                }

                List<double> prefix = [];
                foreach (string item in prefixes)
                {
                    prefix.Add(await Timed(() => StarDictDataAccess.MatchWord(item, 10)).ConfigureAwait(false));
                }

                List<double> pattern = [];
                for (int round = 0; round < 20; round++)
                {
                    foreach (string item in patterns)
                    {
                        pattern.Add(await Timed(() => StarDictDataAccess.PatternWord(item, null, 20)).ConfigureAwait(false));
                    }
                }

                double tagCount = await Timed(async () => { _ = await StarDictDataAccess.GetExtraGlossaryWordsCount("zk").ConfigureAwait(false); }).ConfigureAwait(false);
                double tagList = await Timed(() => StarDictDataAccess.GetAllExtraGlossaryWords("zk", string.Empty, default)).ConfigureAwait(false);
                double oxfordList = await Timed(() => StarDictDataAccess.GetAllExtraGlossaryWords("oxford", string.Empty, default)).ConfigureAwait(false);

                using Process process = Process.GetCurrentProcess();
                Console.WriteLine($"backend={(kind == StarDictBackendKind.Sqlite ? "sqlite" : "csf")}");
                Console.WriteLine($"first_use_ms={open.Elapsed.TotalMilliseconds:F2}");
                Console.WriteLine($"exact={Describe(exact)}");
                Console.WriteLine($"prefix={Describe(prefix)}");
                Console.WriteLine($"pattern={Describe(pattern)}");
                Console.WriteLine($"tag_count_ms={tagCount:F2}");
                Console.WriteLine($"tag_list_ms={tagList:F2}");
                Console.WriteLine($"oxford_list_ms={oxfordList:F2}");
                Console.WriteLine($"ws_mb={process.WorkingSet64 / 1024d / 1024d:F1}");
                Console.WriteLine($"peak_ws_mb={process.PeakWorkingSet64 / 1024d / 1024d:F1}");
                Console.WriteLine($"file_mb={new FileInfo(path).Length / 1024d / 1024d:F2}");
            }

            return 0;
        }

        private static async Task<double> Timed(Func<Task> action)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            await action().ConfigureAwait(false);
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static async Task<double> Timed<T>(Func<Task<T>> action)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            _ = await action().ConfigureAwait(false);
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static string Describe(List<double> values)
        {
            values.Sort();
            double p50 = values[values.Count / 2];
            double p95 = values[(int)Math.Ceiling(values.Count * 0.95) - 1];
            return $"p50={p50:F2}ms p95={p95:F2}ms max={values[^1]:F2}ms";
        }

        private static int CompareIndex(string databasePath, string sql, Action<CsfWordEntryVisitor> iterate, string name, bool isWord)
        {
            int failures = 0;
            long compared = 0;
            byte[] scratch = new byte[1024];
            using SqliteConnection connection = new($"Filename={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using SqliteCommand command = new(sql, connection);
            using SqliteDataReader reader = command.ExecuteReader();

            iterate((ReadOnlySpan<byte> key, int id) =>
            {
                if (!reader.Read())
                {
                    failures++;
                    Console.WriteLine($"FAIL {name}: csf 多余项 id={id} key={Encoding.UTF8.GetString(key)}");
                    return false;
                }

                long expectedId = reader.GetInt64(0);
                string text = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                int byteCount = Encoding.UTF8.GetByteCount(text);
                if (scratch.Length < byteCount)
                {
                    scratch = new byte[byteCount];
                }

                int length = CsfTextFold(text, scratch);
                ReadOnlySpan<byte> expectedKey = scratch.AsSpan(0, length);
                compared++;
                if (expectedId != id || !expectedKey.SequenceEqual(key))
                {
                    failures++;
                    if (failures <= 5)
                    {
                        Console.WriteLine($"FAIL {name}: 期望 id={expectedId} key={Encoding.UTF8.GetString(expectedKey)}，实际 id={id} key={Encoding.UTF8.GetString(key)}");
                    }

                    return failures <= 5;
                }

                return true;
            });

            while (reader.Read())
            {
                failures++;
                if (failures <= 5)
                {
                    Console.WriteLine($"FAIL {name}: csf 缺少 id={reader.GetInt64(0)}");
                }
            }

            Console.WriteLine($"{name}_compared={compared} failures={failures}");
            return failures;
        }

        /// <summary>折叠 ASCII 大小写，与 CsfText.FoldInPlace 一致（此处复刻，避免把内部工具类暴露成公共 API）</summary>
        private static int CsfTextFold(string text, byte[] destination)
        {
            int length = Encoding.UTF8.GetBytes(text, destination);
            for (int i = 0; i < length; i++)
            {
                byte value = destination[i];
                if (value >= (byte)'A' && value <= (byte)'Z')
                {
                    destination[i] = (byte)(value + 32);
                }
            }

            return length;
        }

        private static List<(int Id, string Word)> ReadSamples(string databasePath, int count)
        {
            List<(int, string)> samples = [];
            using SqliteConnection connection = new($"Filename={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using SqliteCommand command = new(
                "select id, word from stardict where id % 673 = 0 order by id limit $limit",
                connection);
            command.Parameters.AddWithValue("$limit", count);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                samples.Add(((int)reader.GetInt64(0), reader.GetString(1)));
            }

            return samples;
        }

        private static StarDictWordItem? ReadSqliteItem(string databasePath, int id)
        {
            using SqliteConnection connection = new($"Filename={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            using SqliteCommand command = new("select * from stardict where id = $id", connection);
            command.Parameters.AddWithValue("$id", id);
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new StarDictWordItem
            {
                Id = reader.GetInt64(0),
                Word = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                StripWord = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Phonetic = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Definition = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Translation = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Exchange = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
            };
        }

        private static bool ItemEquals(StarDictWordItem left, StarDictWordItem right)
        {
            return left.Id == right.Id
                && left.Word == right.Word
                && left.StripWord == right.StripWord
                && left.Phonetic == right.Phonetic
                && left.Definition == right.Definition
                && left.Translation == right.Translation
                && left.Exchange == right.Exchange;
        }

        private static string Format(List<StarDictWordItem> items)
        {
            return string.Join("|", items.Select(item => item.Id.ToString()));
        }

        private static int Report(string label, string expected, string actual)
        {
            Console.WriteLine($"FAIL {label}");
            Console.WriteLine($"  sqlite={Truncate(expected)}");
            Console.WriteLine($"  csf   ={Truncate(actual)}");
            return 1;
        }

        private static string Truncate(string value)
        {
            return value.Length <= 200 ? value : value[..200] + "...";
        }
    }
}
