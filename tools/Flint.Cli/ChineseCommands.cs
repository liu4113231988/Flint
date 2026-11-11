using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Flint3.Data;
using Flint3.Data.Chinese;

namespace Flint.Cli
{
    /// <summary>
    /// 中文反查索引的构建与基准命令。索引是派生物，命令本身不修改 stardict.csf。
    /// </summary>
    internal static class ChineseCommands
    {
        private static readonly string[] ExactSamples =
        [
            "苹果", "银行", "电脑", "苹果树", "一个", "香蕉", "应用", "申请", "中国", "美国",
            "时间", "工作", "学习", "生活", "问题", "世界", "公司", "朋友", "学生", "老师",
            "音乐", "电影", "城市", "旅行", "汽车", "手机", "网络", "计算机", "经济", "政治",
            "文化", "历史", "科学", "技术", "环境", "健康", "家庭", "孩子", "语言", "食物",
        ];

        public static int Build(string databasePath, string indexPath)
        {
            ChineseIndexBuildStats stats = ChineseIndexBuilder.Build(databasePath, indexPath);
            Console.WriteLine("mode=zhbuild");
            Console.WriteLine($"db={databasePath}");
            Console.WriteLine($"index={indexPath}");
            Console.WriteLine($"records={stats.RecordCount}");
            Console.WriteLine($"terms={stats.TermCount}");
            Console.WriteLine($"postings={stats.PostingCount}");
            Console.WriteLine($"entry_mb={stats.EntryBytes / 1024d / 1024d:F1}");
            Console.WriteLine($"file_mb={stats.FileBytes / 1024d / 1024d:F2}");
            Console.WriteLine($"build_s={stats.Elapsed.TotalSeconds:F1}");
            return 0;
        }

        public static async Task<int> BenchAsync(string databasePath, string indexPath)
        {
            Stopwatch open = Stopwatch.StartNew();
            using ChineseIndexReader reader = ChineseIndexReader.Open(indexPath);
            open.Stop();
            if (reader.Status != ChineseIndexStatus.Ready)
            {
                Console.WriteLine($"status={reader.Status} message={reader.ErrorMessage}");
                return 1;
            }

            await StarDictDataAccess.InitializeDatabase(databasePath).ConfigureAwait(false);

            Console.WriteLine("mode=zhbench");
            Console.WriteLine($"index={indexPath}");
            Console.WriteLine($"index_status={reader.Status}");
            Console.WriteLine($"terms={reader.TermCount}");
            Console.WriteLine($"postings={reader.PostingCount}");
            Console.WriteLine($"records={reader.RecordCount}");
            Console.WriteLine($"file_mb={new FileInfo(indexPath).Length / 1024d / 1024d:F2}");
            Console.WriteLine($"open_ms={open.Elapsed.TotalMilliseconds:F2}");

            // 索引侧纯查询延迟（不含回读记录）
            for (int round = 0; round < 2; round++)
            {
                foreach (string term in ExactSamples)
                {
                    _ = reader.LookupExact(term, 10);
                    _ = reader.Lookup(term, 10);
                }
            }

            List<double> exact = [];
            List<double> prefix = [];
            List<double> endToEnd = [];
            for (int round = 0; round < 5; round++)
            {
                foreach (string term in ExactSamples)
                {
                    exact.Add(Timed(() => reader.LookupExact(term, 10)));
                    prefix.Add(Timed(() => reader.Lookup(term, 10)));
                    prefix.Add(Timed(() => reader.Lookup(term[..1], 10)));
                }

                foreach (string term in ExactSamples.Take(20))
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    _ = await StarDictDataAccess.MatchChineseWord(term, 10).ConfigureAwait(false);
                    stopwatch.Stop();
                    endToEnd.Add(stopwatch.Elapsed.TotalMilliseconds);
                }
            }

            Console.WriteLine($"index_exact={Describe(exact)}");
            Console.WriteLine($"index_prefix={Describe(prefix)}");
            Console.WriteLine($"end_to_end={Describe(endToEnd)}");

            foreach (string query in new[] { "苹果", "银行", "电脑", "苹果树", "一个", "应", "银" })
            {
                List<Flint3.Data.Models.StarDictWordItem> items = await StarDictDataAccess.MatchChineseWord(query, 10).ConfigureAwait(false);
                Console.WriteLine($"query={query} count={items.Count} words={string.Join(",", items.Select(item => item.Word))}");
            }

            return 0;
        }

        private static double Timed(Func<List<int>> action)
        {
            long start = Stopwatch.GetTimestamp();
            _ = action();
            return (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }

        private static string Describe(List<double> values)
        {
            values.Sort();
            double p50 = values[values.Count / 2];
            double p95 = values[(int)Math.Ceiling(values.Count * 0.95) - 1];
            return $"p50={p50:F3}ms p95={p95:F3}ms max={values[^1]:F3}ms n={values.Count}";
        }
    }
}
