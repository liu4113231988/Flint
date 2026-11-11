using System;

namespace Flint3.Data.Chinese
{
    /// <summary>
    /// 中文反查索引的状态。Missing/Corrupt/VersionMismatch/SourceMismatch 都按"无结果 + 提示"降级，
    /// 绝不能影响英文查词。
    /// </summary>
    public enum ChineseIndexStatus
    {
        Ready,
        Missing,
        Corrupt,
        VersionMismatch,
        SourceMismatch,
    }

    /// <summary>
    /// 旁挂中文反查索引（stardict.zhx）的格式常量。
    /// 布局：64B header → 词条偏移数组 → 词条区（每条 [len][词条][count][delta 排名...]）→ rank→记录id 表。
    ///
    /// 倒排存的是"全局排名"而不是记录 id：构建期按
    /// Collins 降序 → frq 升序（缺失视为最大）→ 词长升序 → 字母序 给全部记录排一次序，
    /// 于是每条倒排就是一个升序的 uint 排名，查询侧只需做归并即可得到契约顺序，
    /// 排序契约落在索引里，任何"顺手改回"都过不了固定期望的单测。
    /// </summary>
    internal static class ChineseIndexFormat
    {
        /// <summary>"ZHX1" 的小端 uint32</summary>
        public const uint Magic = 0x3158485A;

        public const ushort Version = 1;

        public const int HeaderSize = 64;

        // header 字段偏移
        public const int OffMagic = 0;
        public const int OffVersion = 4;
        public const int OffHeaderSize = 6;
        public const int OffTermCount = 8;
        public const int OffPostingCount = 12;
        public const int OffRecordCount = 16;
        public const int OffTermOffsetsOffset = 24;
        public const int OffEntryDataOffset = 32;
        public const int OffRankIdsOffset = 40;

        /// <summary>Collins 最高 5 星，用 3 bit 存"反星级"，升序即星级降序</summary>
        public const int CollinsBits = 3;
        public const int MaxCollins = 5;

        /// <summary>frq 最大值约 4.7 万，17 bit 足够；全 1 表示无词频，排在已知词频之后</summary>
        public const int FrqBits = 17;
        public const uint UnknownFrq = (1u << FrqBits) - 1;

        /// <summary>词长 8 bit（实测最长 147），0~3 位留空</summary>
        public const int WordLengthBits = 8;
    }

    /// <summary>
    /// 排序键：Collins 反星级(3) | frq(17) | 词长(8) | 保留(4)，整体按 uint 升序即为契约顺序。
    /// </summary>
    internal static class ChineseSortKey
    {
        public static uint Pack(int collins, long? frq, int wordLength)
        {
            uint collinsRank = (uint)(ChineseIndexFormat.MaxCollins - Math.Clamp(collins, 0, ChineseIndexFormat.MaxCollins));
            uint frqRank = frq is > 0 ? (uint)Math.Min(frq.Value, ChineseIndexFormat.UnknownFrq - 1) : ChineseIndexFormat.UnknownFrq;
            uint length = (uint)Math.Clamp(wordLength, 0, 255);
            return (collinsRank << (ChineseIndexFormat.FrqBits + ChineseIndexFormat.WordLengthBits + 4))
                | (frqRank << (ChineseIndexFormat.WordLengthBits + 4))
                | (length << 4);
        }
    }
}
