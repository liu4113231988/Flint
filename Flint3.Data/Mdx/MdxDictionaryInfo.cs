using System.Collections.Generic;

namespace Flint3.Data.Mdx
{
    /// <summary>
    /// 已导入 mdx 词库的登记信息，序列化到 dictionarySources.json
    /// </summary>
    public sealed class MdxDictionaryInfo
    {
        public string Id { get; set; } = "";

        /// <summary>
        /// 展示名称，优先取文件头 Title
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// 复制到 Dicts 目录下的文件名
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// 附带索引库的文件名
        /// </summary>
        public string IndexFileName { get; set; } = "";

        public long EntryCount { get; set; }

        public bool Enabled { get; set; } = true;
    }

    public sealed class MdxDictionarySettings
    {
        public List<MdxDictionaryInfo> Dictionaries { get; set; } = [];
    }
}
