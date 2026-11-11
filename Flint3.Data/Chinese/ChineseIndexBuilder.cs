using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Flint3.Data.Csf;
using Microsoft.Data.Sqlite;

namespace Flint3.Data.Chinese
{
    public sealed class ChineseIndexBuildStats
    {
        public int TermCount { get; init; }
        public int PostingCount { get; init; }
        public int RecordCount { get; init; }
        public long EntryBytes { get; init; }
        public long FileBytes { get; init; }
        public TimeSpan Elapsed { get; init; }
    }

    /// <summary>
    /// 从 ECDICT 的 stardict.db 构建旁挂中文反查索引。离线工具，不参与运行时查询。
    /// 不修改 stardict.csf，也不动 CSF 版本位：索引是纯派生物，删掉只会让中文反查降级。
    /// </summary>
    public static class ChineseIndexBuilder
    {
        public static ChineseIndexBuildStats Build(string databasePath, string indexPath)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            using SqliteConnection connection = new($"Filename={databasePath};Mode=ReadOnly;Pooling=False");
            connection.Open();

            int recordCount = ReadRecordCount(connection);
            if (recordCount <= 0)
            {
                throw new InvalidDataException("源库为空");
            }

            uint[] keys = ReadSortKeys(connection, recordCount);
            int[] wordRank = ReadWordRanks(connection, recordCount);

            // 先给全部记录排一次序：Collins 降序 → frq 升序（缺失最后）→ 词长 → 字母序
            int[] order = new int[recordCount];
            for (int i = 0; i < recordCount; i++)
            {
                order[i] = i + 1;
            }

            Array.Sort(order, (left, right) =>
            {
                int compare = keys[left].CompareTo(keys[right]);
                return compare != 0 ? compare : wordRank[left].CompareTo(wordRank[right]);
            });

            int[] rankOf = new int[recordCount + 1];
            uint[] rankIds = new uint[recordCount];
            for (int i = 0; i < recordCount; i++)
            {
                int id = order[i];
                rankOf[id] = i;
                rankIds[i] = (uint)id;
            }

            keys = null!;
            wordRank = null!;

            Dictionary<string, List<uint>> postings = ReadPostings(connection, rankOf);

            string[] terms = new string[postings.Count];
            postings.Keys.CopyTo(terms, 0);
            // 词条全部是 BMP 汉字，ordinal 顺序与 UTF-8 字节序一致
            Array.Sort(terms, StringComparer.Ordinal);

            int termCount = terms.Length;
            uint[] termOffsets = new uint[termCount + 1];
            long entryDataLength = 0;
            long postingCount = 0;

            for (int i = 0; i < termCount; i++)
            {
                termOffsets[i] = (uint)entryDataLength;

                int termLength = Encoding.UTF8.GetByteCount(terms[i]);
                if (termLength >= 0x80)
                {
                    throw new InvalidDataException("词条长度超出格式约定");
                }

                List<uint> list = postings[terms[i]];
                list.Sort();
                long entryLength = 1 + termLength + VarIntSize((uint)list.Count);
                uint previous = 0;
                foreach (uint rank in list)
                {
                    entryLength += VarIntSize(rank - previous);
                    previous = rank;
                }

                entryDataLength += entryLength;
                postingCount += list.Count;
            }

            termOffsets[termCount] = (uint)entryDataLength;

            if (entryDataLength > uint.MaxValue)
            {
                throw new InvalidDataException("索引超过 4GiB，当前格式不支持");
            }

            long termOffsetsOffset = ChineseIndexFormat.HeaderSize;
            long entryDataOffset = termOffsetsOffset + 4L * (termCount + 1);
            long rankIdsOffset = entryDataOffset + entryDataLength;
            long fileLength = rankIdsOffset + 4L * recordCount;

            byte[] header = new byte[ChineseIndexFormat.HeaderSize];
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(ChineseIndexFormat.OffMagic), ChineseIndexFormat.Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(ChineseIndexFormat.OffVersion), ChineseIndexFormat.Version);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(ChineseIndexFormat.OffHeaderSize), ChineseIndexFormat.HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(ChineseIndexFormat.OffTermCount), (uint)termCount);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(ChineseIndexFormat.OffPostingCount), (uint)postingCount);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(ChineseIndexFormat.OffRecordCount), (uint)recordCount);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(ChineseIndexFormat.OffTermOffsetsOffset), termOffsetsOffset);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(ChineseIndexFormat.OffEntryDataOffset), entryDataOffset);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(ChineseIndexFormat.OffRankIdsOffset), rankIdsOffset);

            byte[] entryData = new byte[entryDataLength];
            byte[] rankIdData = new byte[4L * recordCount];

            int cursor = 0;
            for (int i = 0; i < termCount; i++)
            {
                string term = terms[i];
                int termLength = Encoding.UTF8.GetByteCount(term);
                entryData[cursor++] = (byte)termLength;
                Encoding.UTF8.GetBytes(term, entryData.AsSpan(cursor));
                cursor += termLength;

                List<uint> list = postings[term];
                cursor += VarInt.Write(entryData.AsSpan(cursor), (uint)list.Count);
                uint previous = 0;
                foreach (uint rank in list)
                {
                    cursor += VarInt.Write(entryData.AsSpan(cursor), rank - previous);
                    previous = rank;
                }
            }

            for (int i = 0; i < recordCount; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(rankIdData.AsSpan(i * 4), rankIds[i]);
            }

            using (FileStream stream = new(indexPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                stream.Write(header);
                stream.Write(ToBytes(termOffsets));
                stream.Write(entryData);
                stream.Write(rankIdData);
            }

            stopwatch.Stop();
            return new ChineseIndexBuildStats
            {
                TermCount = termCount,
                PostingCount = (int)postingCount,
                RecordCount = recordCount,
                EntryBytes = entryDataLength,
                FileBytes = fileLength,
                Elapsed = stopwatch.Elapsed,
            };
        }

        private static int ReadRecordCount(SqliteConnection connection)
        {
            using SqliteCommand command = new("select count(*) from stardict", connection);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        private static uint[] ReadSortKeys(SqliteConnection connection, int recordCount)
        {
            uint[] keys = new uint[recordCount + 1];
            using SqliteCommand command = new("select id, length(word), collins, frq from stardict order by id", connection);
            using SqliteDataReader reader = command.ExecuteReader();
            int expected = 1;
            while (reader.Read())
            {
                int id = (int)reader.GetInt64(0);
                if (id != expected)
                {
                    throw new InvalidDataException($"源库 id 不连续：期望 {expected}，实际 {id}");
                }

                int wordLength = reader.IsDBNull(1) ? 0 : (int)reader.GetInt64(1);
                int collins = reader.IsDBNull(2) ? 0 : (int)reader.GetInt64(2);
                long? frq = reader.IsDBNull(3) ? null : reader.GetInt64(3);
                keys[id] = ChineseSortKey.Pack(collins, frq, wordLength);
                expected++;
            }

            return keys;
        }

        /// <summary>记录 id → 词头 NOCASE 序名次；用它做排序键的字母序兜底，避免构建期常驻 340 万个词头字符串</summary>
        private static int[] ReadWordRanks(SqliteConnection connection, int recordCount)
        {
            int[] ranks = new int[recordCount + 1];
            using SqliteCommand command = new("select id from stardict order by word collate nocase", connection);
            using SqliteDataReader reader = command.ExecuteReader();
            int rank = 0;
            while (reader.Read())
            {
                ranks[(int)reader.GetInt64(0)] = rank++;
            }

            return ranks;
        }

        private static Dictionary<string, List<uint>> ReadPostings(SqliteConnection connection, int[] rankOf)
        {
            Dictionary<string, List<uint>> postings = new(StringComparer.Ordinal);
            using SqliteCommand command = new(
                "select id, translation from stardict where translation is not null and translation <> ''",
                connection);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(0);
                string translation = reader.GetString(1);
                if (!ChineseTermExtractor.ContainsChinese(translation))
                {
                    continue;
                }

                uint rank = (uint)rankOf[id];
                foreach (string term in ChineseTermExtractor.Extract(translation))
                {
                    if (!postings.TryGetValue(term, out List<uint>? list))
                    {
                        list = new List<uint>(2);
                        postings.Add(term, list);
                    }

                    list.Add(rank);
                }
            }

            return postings;
        }

        private static int VarIntSize(uint value)
        {
            int size = 1;
            while (value >= 0x80)
            {
                value >>= 7;
                size++;
            }

            return size;
        }

        private static byte[] ToBytes(uint[] values)
        {
            byte[] bytes = new byte[4L * values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]);
            }

            return bytes;
        }
    }
}
