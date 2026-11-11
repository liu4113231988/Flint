namespace Flint3.Data.Csf
{
    /// <summary>
    /// CSF 只读词典文件格式常量。单文件、只读、可直接 mmap。
    /// 布局：256B header → 记录块数据 → 记录块目录 → 词头索引数据 → 词头倒排目录
    ///       → sw 索引数据 → sw 索引目录 → 首键表 → oxford 位图 → tag 取值表 → tag 索引
    /// </summary>
    internal static class CsfFormat
    {
        /// <summary>"CSF1" 的小端 uint32</summary>
        public const uint Magic = 0x31465343;

        public const ushort Version = 1;

        public const int HeaderSize = 256;

        /// <summary>块目录项：offset(8) + compressedLength(4) + rawLength(4) + checksum(4)</summary>
        public const int DirectoryEntrySize = 20;

        // header 字段偏移
        public const int OffMagic = 0;
        public const int OffVersion = 4;
        public const int OffHeaderSize = 6;
        public const int OffRecordsPerBlock = 8;
        public const int OffIndexBlockSize = 12;
        public const int OffRecordCount = 16;
        public const int OffRecordBlockCount = 20;
        public const int OffWordIndexBlockCount = 24;
        public const int OffSwIndexBlockCount = 28;
        public const int OffTagValueCount = 32;
        public const int OffRecordDirOffset = 40;
        public const int OffWordDirOffset = 48;
        public const int OffSwDirOffset = 56;
        public const int OffWordIndexOffset = 64;
        public const int OffWordIndexLength = 72;
        public const int OffSwIndexOffset = 80;
        public const int OffSwIndexLength = 88;
        public const int OffWordFirstKeysOffset = 96;
        public const int OffWordFirstKeysLength = 104;
        public const int OffSwFirstKeysOffset = 112;
        public const int OffSwFirstKeysLength = 120;
        public const int OffOxfordOffset = 128;
        public const int OffOxfordLength = 136;
        public const int OffOxfordRawLength = 144;
        public const int OffTagValuesOffset = 152;
        public const int OffTagValuesLength = 160;
        public const int OffTagValuesRawLength = 168;
        public const int OffTagIndexOffset = 176;
        public const int OffTagIndexLength = 184;
        public const int OffTagIndexRawLength = 192;
        public const int OffFileLength = 200;
    }
}
