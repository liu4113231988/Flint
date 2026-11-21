using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Flint3.Data.Tests;

public class GlossaryDeletionTests
{
    [Fact]
    public async Task FailedDeletionRollsBackWordsAndCategory()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".db");
        try
        {
            await GlossaryDataAccess.LoadDatabase(path);
            using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO glossaryCategory(id,title) VALUES(1,'test');
                INSERT INTO glossary(glossaryid,word) VALUES(1,'apple');
                CREATE TRIGGER block_delete BEFORE DELETE ON glossaryCategory
                BEGIN SELECT RAISE(ABORT,'test'); END;
                """;
            command.ExecuteNonQuery();
            await Assert.ThrowsAsync<SqliteException>(() => GlossaryDataAccess.DeleteGlossary(1));
            command.CommandText = "SELECT COUNT(*) FROM glossary JOIN glossaryCategory ON glossaryid=glossaryCategory.id";
            Assert.Equal(1L, command.ExecuteScalar());

            command.CommandText = "DROP TRIGGER block_delete";
            command.ExecuteNonQuery();
            await GlossaryDataAccess.DeleteGlossary(1);
            command.CommandText = "SELECT (SELECT COUNT(*) FROM glossary)+(SELECT COUNT(*) FROM glossaryCategory)";
            Assert.Equal(0L, command.ExecuteScalar());
        }
        finally
        {
            GlossaryDataAccess.CloseDatabase();
            using var pooled = new SqliteConnection($"Filename={path}");
            SqliteConnection.ClearPool(pooled);
            File.Delete(path);
        }
    }
}
