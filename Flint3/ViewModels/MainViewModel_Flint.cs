using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Flint3.Core.Query;
using Flint3.Data;
using Flint3.Data.Models;
using Flint3.Data.Sources;

namespace Flint3.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private const int SearchDebounceMilliseconds = 300;

        [System.Text.RegularExpressions.GeneratedRegex(@"[^\w]*")]
        private static partial System.Text.RegularExpressions.Regex WordSearchRegex();

        private readonly WordQueryService _wordQueryService = new(new IWordSource[] { new EcdictSource() });

        private readonly SearchCoordinator _searchCoordinator = new();

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
            // 预热数据库连接；查询方法会等待同一个初始化任务，避免启动后立即输入时查不到词
            _ = StarDictDataAccess.InitializeDatabase();
        }

        /// <summary>
        /// 查找模糊匹配的多个单词：新输入会取消上一次请求，延迟 300ms 后只保留最后一次结果
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

            _ = SearchWordAsync(WordSearchRegex().Replace(word, ""), wordsCollection, limit, request);
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
