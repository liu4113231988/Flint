namespace Flint3.Core.Query
{
    /// <summary>
    /// 各词典源输出的统一词条，UI 层负责再转换为其展示模型
    /// </summary>
    public sealed class WordEntry
    {
        public long Id { get; init; }

        public string Word { get; init; } = "";

        public string StripWord { get; init; } = "";

        public string Phonetic { get; init; } = "";

        public string Definition { get; init; } = "";

        public string Translation { get; init; } = "";

        public string Exchange { get; init; } = "";

        /// <summary>
        /// 来源标记，如 Ecdict、Mdx:xxx
        /// </summary>
        public string Source { get; init; } = "";
    }
}
