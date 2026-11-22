using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Query;
using Flint3.Data;
using Flint3.Data.Chinese;
using Flint3.Data.Mdx;
using Flint3.Data.Models;
using Flint3.Data.Sources;

namespace Flint3.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const int SearchDebounceMilliseconds = 300;

        private const int PatternSearchLimit = 40;

        private readonly MdxDictionaryStore _mdxDictionaryStore = new();

        private WordQueryService _wordQueryService;
        private bool _hasImportedSources;

        private readonly SearchCoordinator _searchCoordinator = new();
        private readonly SearchCoordinator _liteSearchCoordinator = new();

        /// <summary>
        /// mdx 词库的导入、删除与启用开关都由设置页经此访问
        /// </summary>
        public MdxDictionaryStore DictionaryStore => _mdxDictionaryStore;

        /// <summary>
        /// 词典列表变化后重建查询源，并释放旧源持有的文件句柄
        /// </summary>
        public void RefreshDictionarySources()
        {
            _searchCoordinator.Cancel();
            _liteSearchCoordinator.Cancel();
            WordQueryService previous = _wordQueryService;
            _wordQueryService = CreateQueryService();
            previous.Dispose();
            if (_lastRawInput.Length > 0)
            {
                MatchWord(_lastRawInput, searchingFromMain: true);
            }
        }

        private WordQueryService CreateQueryService()
        {
            List<IWordSource> sources = [new EcdictSource()];
            sources.AddRange(_mdxDictionaryStore.CreateSources());
            _hasImportedSources = sources.Count > 1;
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

        private string _searchHint = string.Empty;

        /// <summary>
        /// 无结果时的可读提示（中文反查索引缺失/损坏时说明原因），有结果时为空
        /// </summary>
        public string SearchHint
        {
            get => _searchHint;
            private set => SetProperty(ref _searchHint, value);
        }

        private string _liteSearchHint = string.Empty;

        public string LiteSearchHint
        {
            get => _liteSearchHint;
            private set => SetProperty(ref _liteSearchHint, value);
        }

        /// <summary>
        /// 进行对单词搜索相关的初始化
        /// </summary>
        private void InitViewModel4Flint()
        {
            _wordQueryService = CreateQueryService();
            // 词库后端在首次查询时惰性创建（CSF 走 mmap，打开成本低），无需预热
        }

        /// <summary>
        /// 查找模糊匹配的多个单词：新输入会取消上一次请求，延迟 300ms 后只保留最后一次结果。
        /// 输入含通配符语法（<c>?</c> / <c>*</c> / <c>_</c> / <c>^</c> / <c>$</c>）时改走模式查询。
        /// </summary>
        /// <param name="word"></param>
        public void MatchWord(string word, bool searchingFromMain, int limit = 10)
        {
            ObservableCollection<StarDictWordItem> wordsCollection = searchingFromMain ? this.SearchResultWordItems : this.LiteSearchResultWordItems;
            SearchRequest request = (searchingFromMain ? _searchCoordinator : _liteSearchCoordinator).BeginRequest();
            SetSearchHint(wordsCollection, string.Empty);
            if (searchingFromMain)
            {
                UpdateSentenceState(word);
            }

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

            _ = SearchWordAsync(trimmed, wordsCollection, limit, request);
        }

        /// <summary>
        /// 合法的通配符输入走模式查询
        /// </summary>
        private static bool IsPatternInput(string input)
        {
            return input.IndexOfAny(['?', '*', '_', '^', '$']) >= 0
                && WordPattern.TryTranslate(input, out _);
        }

        internal void CancelLiteSearch() => _liteSearchCoordinator.Cancel();

        internal Task PreviewCapturedTextAsync(string text)
        {
            SearchRequest request = _liteSearchCoordinator.BeginRequest();
            LiteSearchResultWordItems.Clear();
            LiteSearchHint = string.Empty;
            return SearchWordAsync(text, LiteSearchResultWordItems, 10, request, debounce: false);
        }

        private async Task SearchPatternAsync(string pattern, ObservableCollection<StarDictWordItem> wordsCollection, int limit, SearchRequest request)
        {
            try
            {
                await Task.Delay(SearchDebounceMilliseconds, request.Token);

                IReadOnlyList<WordEntry> entries = await _wordQueryService.PatternAsync(pattern, limit, request.Token);
                request.Token.ThrowIfCancellationRequested();

                string hint = await EmptyResultHintAsync(pattern, entries.Count, request.Token);
                PublishResults(wordsCollection, entries, request.Sequence, hint);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        private async Task SearchWordAsync(string word, ObservableCollection<StarDictWordItem> wordsCollection, int limit, SearchRequest request, bool debounce = true)
        {
            try
            {
                if (debounce)
                {
                    await Task.Delay(SearchDebounceMilliseconds, request.Token);
                }

                bool reverseLookup = ChineseTermExtractor.TryNormalizeQuery(word, out _);
                IReadOnlyList<WordEntry> entries = await _wordQueryService.LookupAsync(word, limit, reverseLookup, request.Token);
                request.Token.ThrowIfCancellationRequested();

                string hint = await EmptyResultHintAsync(word, entries.Count, request.Token);
                PublishResults(wordsCollection, entries, request.Sequence, hint);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        }

        /// <summary>
        /// 切回 UI 线程并再次校验序号，避免旧请求覆盖新结果
        /// </summary>
        private void PublishResults(ObservableCollection<StarDictWordItem> wordsCollection, IReadOnlyList<WordEntry> entries, long sequence, string hint)
        {
            void Apply()
            {
                SearchCoordinator coordinator = ReferenceEquals(wordsCollection, LiteSearchResultWordItems)
                    ? _liteSearchCoordinator
                    : _searchCoordinator;
                if (!coordinator.IsCurrent(sequence))
                {
                    return;
                }

                wordsCollection.Clear();
                foreach (WordEntry entry in entries)
                {
                    wordsCollection.Add(MakeupWordItem(entry));
                }

                SetSearchHint(wordsCollection, hint);
            }

            if (this.Dispatcher is null || this.Dispatcher.HasThreadAccess)
            {
                Apply();
                return;
            }

            this.Dispatcher.TryEnqueue(Apply);
        }

        private void SetSearchHint(ObservableCollection<StarDictWordItem> wordsCollection, string hint)
        {
            if (ReferenceEquals(wordsCollection, LiteSearchResultWordItems))
            {
                LiteSearchHint = hint;
            }
            else
            {
                SearchHint = hint;
            }
        }

        private async Task<string> EmptyResultHintAsync(string word, int resultCount, System.Threading.CancellationToken cancellationToken)
        {
            if (resultCount > 0)
            {
                return string.Empty;
            }

            if (!_hasImportedSources && !await StarDictDataAccess.IsDictionaryAvailableAsync(cancellationToken))
            {
                return "没有可用词库。请将 stardict.csf 放入程序的 Data 目录，或在设置中导入 MDX 词库。";
            }

            if (ChineseTermExtractor.TryNormalizeQuery(word, out _))
            {
                return StarDictDataAccess.ChineseLookupStatus == ChineseIndexStatus.Ready
                    ? "未找到对应的英文词"
                    : $"中文反查不可用：{StarDictDataAccess.ChineseLookupHint}";
            }

            return "本地词典未找到释义";
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
