using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Flint3.Data.Tests
{
    /// <summary>
    /// 真实词典库的结构校验。默认跳过，由 tools/prepare-data.ps1 设置 FLINT_STARDICT_DB 后单独运行
    /// </summary>
    public class RealDatabaseTests
    {
        [Fact]
        public void 真实库词条数充足且具有可用索引()
        {
            string? path = Environment.GetEnvironmentVariable("FLINT_STARDICT_DB");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            Assert.True(File.Exists(path), $"数据库不存在: {path}");

            using var connection = new SqliteConnection($"Filename={path}");
            connection.Open();

            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "select count(*) from stardict";
            long count = Convert.ToInt64(countCommand.ExecuteScalar());
            Assert.True(count > 100000, $"词条数过少: {count}");

            // 输入即查依赖 sw 列索引，缺失会退化为全表扫描
            Assert.True(HasIndexOnColumn(connection, "stardict", "sw"), "stardict 缺少 sw 列索引");
        }

        private static bool HasIndexOnColumn(SqliteConnection connection, string table, string column)
        {
            var indexNames = new List<string>();
            using (var listCommand = connection.CreateCommand())
            {
                listCommand.CommandText = $"PRAGMA index_list({table})";
                using var reader = listCommand.ExecuteReader();
                while (reader.Read())
                {
                    indexNames.Add(reader.GetString(1));
                }
            }

            foreach (string indexName in indexNames)
            {
                using var infoCommand = connection.CreateCommand();
                infoCommand.CommandText = $"PRAGMA index_info({indexName})";
                using var reader = infoCommand.ExecuteReader();
                while (reader.Read())
                {
                    string columnName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    if (string.Equals(columnName, column, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
