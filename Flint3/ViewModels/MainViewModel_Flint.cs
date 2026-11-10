using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Query;
using Flint3.Data;
using Flint3.Data.Mdx;
using Flint3.Data.Models;
using Flint3.Data.Sources;

namespace Flint3.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const int SearchDebounceMilliseconds = 300;

        private const int PatternSearchLimit = 40;

        [System.Text.RegularExpressions.GeneratedRegex(@"[^\w]*")]
        private static partial System.Text.RegularExpressions.Regex WordSearchRegex();

        private readonly MdxDictionaryStore _mdxDictionaryStore = new();

        private WordQueryService _wordQueryService;

        private readonly SearchCoordinator _searchCoordinator = new();

        /// <summary>
        /// mdx 词库的导入、删除与启用开关都由设置页经此访问
        /// </summary>
        public MdxDictionaryStore DictionaryStore => _mdxDictionaryStore;

        /// <summary>
        /// 词典列表变化后重建查询源，并释放旧源持有的文件句柄
        /// </summary>
        public void RefreshDictionarySources()
        {
            WordQueryService previous = _wordQueryService;
            _wordQueryService = CreateQueryService();
            previous.Dispose();
        }

        private WordQueryService CreateQueryService()
        {
            List<IWordSource> sources = [new EcdictSource()];
            sources.AddRange(_mdxDictionaryStore.CreateSources());
            return new WordQueryService(sources);
        }

        /// <summary>
        /// 查词结果
        /// </summary>
        public ObservableCollection<StarDictWordItem> SearchResultWordItems { get; private set; } = new ObservableCollection<StarDictWordItem>();

        /// <summary>
        /// 查词结果
        /// </summary>
        public ObservableCollection<StarDictWordItem> LiteSearchResultWordItems { get; private set; } = new ObservableCollection<StarDictWordItem>();

        /// <summary>
        /// 进行对单词搜索相关的初始化
        /// </summary>
        private void InitViewModel4Flint()
        {
            _wordQueryService = CreateQueryService();

            // 预热数据库连接；查询方法会等待同一个初始化任务，避免启动后立即输入时查不到词
            _ = StarDictDataAccess.InitializeDatabase();
        }

        /// <summary>
        /// 查找模糊匹配的多个单词：新输入会取消上一次请求，延迟 300ms 后只保留最后一次结果。
        /// 输入含通配符语法（<c>?</c> / <c>*</c> / <c>_</c> / <c>^</c> / <c>$</c>）时改走模式查询。
        /// </summary>
        /// <param name="word"></param>
        public void MatchWord(string word, bool searchingFromMain, int limit = 10)
        {
            ObservableCollection<StarDictWordItem> wordsCollection = searchingFromMain ? this.SearchResultWordItems : this.LiteSearchResultWordItems;
            SearchRequest request = _searchCoordinator.BeginRequest();

            if (string.IsNullOrWhiteSpace(word))
            {
                wordsCollection.Clear();
                return;
            }

            string trimmed = word.Trim();
            if (IsPatternInput(trimmed))
            {
                _ = SearchPatternAsync(trimmed, wordsCollection, PatternSearchLimit, request);
                return;
            }

            _ = SearchWordAsync(WordSearchRegex().Replace(word, ""), wordsCollection, limit, request);
        }

        /// <summary>
        /// 通配符输入不能交给 WordSearchRegex 清洗，否则 ? 与 * 会被直接删掉
        /// </summary>
        private static bool IsPatternInput(string input)
        {
            return input.IndexOfAny(['?', '*', '_', '^', '$']) >= 0;
        }

        private async Task SearchPatternAsync(string pattern, ObservableCollection<StarDictWordItem> wordsCollection, int limit, SearchRequest request)
        {
            try
            {
                await Task.Delay(SearchDebounceMilliseconds, request.Token);

                IReadOnlyList<WordEntry> entries = await _wordQueryService.PatternAsync(pattern, limit, request.Token);
                request.Token.ThrowIfCancellationRequested();

                PublishResults(wordsCollection, entries, request.Sequence);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        private async Task SearchWordAsync(string word, ObservableCollection<StarDictWordItem> wordsCollection, int limit, SearchRequest request)
        {
            try
            {
                await Task.Delay(SearchDebounceMilliseconds, request.Token);

                IReadOnlyList<WordEntry> entries = await _wordQueryService.MatchAsync(word, limit, request.Token);
                request.Token.ThrowIfCancellationRequested();

                PublishResults(wordsCollection, entries, request.Sequence);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        /// <summary>
        /// 切回 UI 线程并再次校验序号，避免旧请求覆盖新结果
        /// </summary>
        private void PublishResults(ObservableCollection<StarDictWordItem> wordsCollection, IReadOnlyList<WordEntry> entries, long sequence)
        {
            void Apply()
            {
                if (!_searchCoordinator.IsCurrent(sequence))
                {
                    return;
                }

                wordsCollection.Clear();
                foreach (WordEntry entry in entries)
                {
                    wordsCollection.Add(MakeupWordItem(entry));
                }
            }

            if (this.Dispatcher is null)
            {
                Apply();
                return;
            }

            this.Dispatcher.TryEnqueue(Apply);
        }

        /// <summary>
        /// 将查询结果转换为界面展示的数据类型
        /// </summary>
        private StarDictWordItem MakeupWordItem(WordEntry entry)
        {
            return MakeupWordItem(new StarDictWordItem
            {
                Id = entry.Id,
                Word = entry.Word,
                StripWord = entry.StripWord,
                Phonetic = entry.Phonetic,
                Definition = entry.Definition,
                Translation = entry.Translation,
                Exchange = entry.Exchange,
                Source = entry.Source
            });
        }

        /// <summary>
        /// 将数据库返回的数据转换为界面展示的数据类型
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private StarDictWordItem MakeupWordItem(StarDictWordItem item)
        {
            try
            {
                if (this.AppSettings.EnableEngDefinition == false)
                {
                    item.Definition = string.Empty;
                }

                IReadOnlyList<WordExchange> exchanges = ExchangeParser.Parse(item.Exchange);
                if (exchanges.Count > 0)
                {
                    item.Exchanges = new List<WordExchangeItem>(exchanges.Count);
                    foreach (WordExchange exchange in exchanges)
                    {
                        item.Exchanges.Add(new WordExchangeItem(exchange.Word, exchange.Type));
                    }
                }
                else
                {
                    item.Exchanges = null;
                }

                return item;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(ex);
                return item;
            }
        }
    }
}
