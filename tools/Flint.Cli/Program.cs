using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Flint3.Core.Input;
using Flint3.Core.Query;
using Flint3.Core.Serialization;
using Flint3.Data;
using Flint3.Data.Sources;

namespace Flint.Cli
{
    /// <summary>
    /// AOT 验证用的最小查询 harness：同一份可执行文件既可作为 JIT 自包含基线，也可按 NativeAOT 发布后对比
    /// 用法：Flint.Cli &lt;dbPath&gt; &lt;match|query|pattern|bench&gt; &lt;word&gt; [limit]
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Stopwatch total = Stopwatch.StartNew();

            if (args.Length >= 3 && string.Equals(args[1], "build", StringComparison.OrdinalIgnoreCase))
            {
                return CsfCommands.Build(args[0], args[2]);
            }

            if (args.Length >= 3 && string.Equals(args[1], "verify", StringComparison.OrdinalIgnoreCase))
            {
                bool deep = args.Length > 3 && string.Equals(args[3], "deep", StringComparison.OrdinalIgnoreCase);
                return await CsfCommands.VerifyAsync(args[0], args[2], deep).ConfigureAwait(false);
            }

            if (args.Length >= 3 && string.Equals(args[1], "csfbench", StringComparison.OrdinalIgnoreCase))
            {
                return await CsfCommands.BenchAsync(args[0], args[2]).ConfigureAwait(false);
            }

            if (args.Length < 3)
            {
                Console.WriteLine("usage: Flint.Cli <dbPath> <match|query|pattern|bench> <word> [limit]");
                Console.WriteLine("       Flint.Cli <dbPath> build <csfPath>");
                Console.WriteLine("       Flint.Cli <dbPath> verify <csfPath> [deep]");
                Console.WriteLine("       Flint.Cli <dbPath> csfbench <csfPath>");
                return 2;
            }

            string dbPath = args[0];
            string mode = args[1].ToLowerInvariant();
            string word = args[2];
            int limit = args.Length > 3 && int.TryParse(args[3], out int parsed) ? parsed : 10;

            Stopwatch init = Stopwatch.StartNew();
            await StarDictDataAccess.InitializeDatabase(dbPath).ConfigureAwait(false);
            init.Stop();

            // 先预热一次，避免把首次连接/页缓存代价算进查询耗时
            WordQueryService service = new(new IWordSource[] { new EcdictSource() });
            _ = await service.MatchAsync(word, limit).ConfigureAwait(false);

            Stopwatch query = Stopwatch.StartNew();
            string summary;
            int count;

            if (mode == "bench")
            {
                // 顺序执行 200 次查询，模拟连续输入时的单次延迟分布
                string[] words = { "app", "apple", "banana", "run", "time", "a", "make", "take", "good", "work" };
                List<double> latencies = new(200);
                int empty = 0;
                count = 0;
                for (int i = 0; i < 200; i++)
                {
                    Stopwatch single = Stopwatch.StartNew();
                    IReadOnlyList<WordEntry> results = await service.MatchAsync(words[i % words.Length], limit).ConfigureAwait(false);
                    single.Stop();
                    latencies.Add(single.Elapsed.TotalMilliseconds);
                    count += results.Count;
                    if (results.Count == 0) empty++;
                }
                latencies.Sort();
                double p50 = latencies[latencies.Count / 2];
                double p95 = latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];
                summary = $"ok={latencies.Count} empty={empty} count={count} p50_ms={p50:F2} p95_ms={p95:F2}";
            }
            else if (mode == "json")
            {
                // 验证源生成 JSON 上下文与 CsWin32 生成的按键名解析在 NativeAOT 下可用
                HotkeySettings settings = new(true, true, false, false, 0x41);
                string json = JsonSerializer.Serialize(settings, FlintJsonContext.Default.HotkeySettings);
                HotkeySettings? roundtrip = JsonSerializer.Deserialize(json, FlintJsonContext.Default.HotkeySettings);
                summary = $"json={json} roundtrip='{roundtrip?.ToString()}'";
                count = roundtrip is null ? 0 : 1;
            }
            else
            {
                IReadOnlyList<WordEntry> results = mode switch
                {
                    "query" => await service.QueryAsync(word).ConfigureAwait(false),
                    "pattern" => await service.PatternAsync(word, limit).ConfigureAwait(false),
                    _ => await service.MatchAsync(word, limit).ConfigureAwait(false),
                };
                count = results.Count;
                summary = "words=" + string.Join(",", results.Take(limit).Select(r => r.Word));
            }

            query.Stop();
            total.Stop();

            using Process process = Process.GetCurrentProcess();
            Console.WriteLine($"mode={mode}");
            Console.WriteLine($"db={dbPath}");
            Console.WriteLine($"init_ms={init.ElapsedMilliseconds}");
            Console.WriteLine($"query_ms={query.ElapsedMilliseconds}");
            Console.WriteLine($"count={count}");
            Console.WriteLine(summary);
            Console.WriteLine($"ws_mb={process.WorkingSet64 / 1024d / 1024d:F1}");
            Console.WriteLine($"peak_ws_mb={process.PeakWorkingSet64 / 1024d / 1024d:F1}");
            Console.WriteLine($"total_ms={total.ElapsedMilliseconds}");
            return count > 0 ? 0 : 1;
        }
    }
}
