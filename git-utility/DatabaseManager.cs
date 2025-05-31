using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SQLite;
using System.IO;
using System.Threading.Tasks;

namespace GitUtility
{
    public class DatabaseManager
    {
        private readonly string _connectionString;
        private readonly string _dbPath;

        public DatabaseManager()
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitUtility");
            Directory.CreateDirectory(appDataPath);

            _dbPath = Path.Combine(appDataPath, "commits.db");
            _connectionString = $"Data Source={_dbPath};Version=3;";

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var connection = new SQLiteConnection(_connectionString);
            connection.Open();

            var createCommitsTable = @"
                CREATE TABLE IF NOT EXISTS Commits (
                    Id TEXT PRIMARY KEY,
                    Number INTEGER NOT NULL,
                    Hash TEXT NOT NULL,
                    Message TEXT NOT NULL,
                    Timestamp TEXT NOT NULL,
                    ParentId TEXT,
                    BranchName TEXT NOT NULL,
                    FOREIGN KEY (ParentId) REFERENCES Commits(Id)
                );";

            var createActiveCommitTable = @"
                CREATE TABLE IF NOT EXISTS ActiveCommit (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    CommitId TEXT NOT NULL,
                    FOREIGN KEY (CommitId) REFERENCES Commits(Id)
                );";

            using var command = new SQLiteCommand(createCommitsTable + createActiveCommitTable, connection);
            command.ExecuteNonQuery();
        }

        public async Task SaveCommit(CommitNode commit)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT OR REPLACE INTO Commits (Id, Number, Hash, Message, Timestamp, ParentId, BranchName)
                VALUES (@Id, @Number, @Hash, @Message, @Timestamp, @ParentId, @BranchName)";

            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", commit.Id.ToString());
            command.Parameters.AddWithValue("@Number", commit.Number);
            command.Parameters.AddWithValue("@Hash", commit.Hash);
            command.Parameters.AddWithValue("@Message", commit.Message);
            command.Parameters.AddWithValue("@Timestamp", commit.Timestamp.ToString("O"));
            command.Parameters.AddWithValue("@ParentId", commit.ParentId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@BranchName", commit.BranchName);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<CommitNode> GetCommit(Guid id)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Commits WHERE Id = @Id";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id.ToString());

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return CreateCommitFromReader(reader);
            }

            return null;
        }

        public async Task<CommitNode> GetCommitByNumber(int number)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Commits WHERE Number = @Number";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@Number", number);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return CreateCommitFromReader(reader);
            }

            return null;
        }

        public async Task<List<CommitNode>> GetAllCommits()
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Commits ORDER BY Number";
            using var command = new SQLiteCommand(sql, connection);

            var commits = new List<CommitNode>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                commits.Add(CreateCommitFromReader(reader));
            }

            return commits;
        }

        public async Task SetActiveCommit(Guid commitId)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "INSERT OR REPLACE INTO ActiveCommit (Id, CommitId) VALUES (1, @CommitId)";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@CommitId", commitId.ToString());

            await command.ExecuteNonQueryAsync();
        }

        public async Task<Guid?> GetActiveCommitId()
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT CommitId FROM ActiveCommit WHERE Id = 1";
            using var command = new SQLiteCommand(sql, connection);

            var result = await command.ExecuteScalarAsync();
            if (result != null && Guid.TryParse(result.ToString(), out var commitId))
            {
                return commitId;
            }

            return null;
        }

        public async Task<int> GetNextCommitNumber()
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT COALESCE(MAX(Number), 0) + 1 FROM Commits";
            using var command = new SQLiteCommand(sql, connection);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task ClearCommitTree()
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var deleteActiveCommit = "DELETE FROM ActiveCommit";
                using var command1 = new SQLiteCommand(deleteActiveCommit, connection, transaction);
                await command1.ExecuteNonQueryAsync();

                var deleteCommits = "DELETE FROM Commits";
                using var command2 = new SQLiteCommand(deleteCommits, connection, transaction);
                await command2.ExecuteNonQueryAsync();

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<int> GetChildrenCount(Guid parentId)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT COUNT(*) FROM Commits WHERE ParentId = @ParentId";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@ParentId", parentId.ToString());

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        private CommitNode CreateCommitFromReader(DbDataReader reader)
        {
            var parentIdStr = reader["ParentId"] as string;
        Guid? parentId = null;
            if (!string.IsNullOrEmpty(parentIdStr))
            {
                parentId = Guid.Parse(parentIdStr);
            }

            return new CommitNode
            {
                Id = Guid.Parse(reader["Id"].ToString()),
                Number = Convert.ToInt32(reader["Number"]),
                Hash = reader["Hash"].ToString(),
                Message = reader["Message"].ToString(),
                Timestamp = DateTime.Parse(reader["Timestamp"].ToString()),
                ParentId = parentId,
                BranchName = reader["BranchName"].ToString()
            };
        }
    }
}