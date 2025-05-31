using System.Data.Common;
using System.Data.SQLite;

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

            var createRepositoryTable = @"
                CREATE TABLE IF NOT EXISTS Repositories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Path TEXT NOT NULL
                );";

            var createCommitsTable = @"
                CREATE TABLE IF NOT EXISTS Commits (
                    Id TEXT PRIMARY KEY,
                    RepositoryId INTEGER NOT NULL,
                    Number INTEGER NOT NULL,
                    Hash TEXT NOT NULL,
                    Message TEXT NOT NULL,
                    Timestamp TEXT NOT NULL,
                    ParentId TEXT,
                    BranchName TEXT NOT NULL,
                    FOREIGN KEY (ParentId) REFERENCES Commits(Id),
                    FOREIGN KEY (RepositoryId) REFERENCES Repositories(Id)
                );";

            var createActiveCommitTable = @"
                CREATE TABLE IF NOT EXISTS ActiveCommit (
                    RepositoryId INTEGER PRIMARY KEY,
                    CommitId TEXT NOT NULL,
                    FOREIGN KEY (CommitId) REFERENCES Commits(Id)
                );";

            using var command = new SQLiteCommand(createRepositoryTable + createCommitsTable + createActiveCommitTable, connection);
            command.ExecuteNonQuery();
        }

        public async Task SaveCommit(CommitNode commit)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = @"
                INSERT OR REPLACE INTO Commits (Id, RepositoryId, Number, Hash, Message, Timestamp, ParentId, BranchName)
                VALUES (@Id, @RepositoryId, @Number, @Hash, @Message, @Timestamp, @ParentId, @BranchName)";

            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", commit.Id.ToString());
            command.Parameters.AddWithValue("@RepositoryId", commit.RepositoryId);
            command.Parameters.AddWithValue("@Number", commit.Number);
            command.Parameters.AddWithValue("@Hash", commit.Hash);
            command.Parameters.AddWithValue("@Message", commit.Message);
            command.Parameters.AddWithValue("@Timestamp", commit.Timestamp.ToString("O"));
            command.Parameters.AddWithValue("@ParentId", commit.ParentId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@BranchName", commit.BranchName);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<CommitNode?> GetCommit(Guid id)
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

        public async Task<CommitNode?> GetCommitByNumber(int repositoryId, int number)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Commits WHERE Number = @Number AND RepositoryId = @RepositoryId";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@Number", number);
            command.Parameters.AddWithValue("@RepositoryId", repositoryId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return CreateCommitFromReader(reader);
            }

            return null;
        }

        public async Task<List<CommitNode>> GetAllCommits(int repositoryId)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT * FROM Commits WHERE RepositoryId = @RepositoryId ORDER BY Number";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@RepositoryId", repositoryId);

            var commits = new List<CommitNode>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                commits.Add(CreateCommitFromReader(reader));
            }

            return commits;
        }

        public async Task SetActiveCommit(int repositoryId, Guid commitId)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "INSERT OR REPLACE INTO ActiveCommit (RepositoryId, CommitId) VALUES (@RepositoryId, @CommitId)";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@CommitId", commitId.ToString());
            command.Parameters.AddWithValue("@RepositoryId", repositoryId);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<Guid?> GetActiveCommitId(int repositoryId)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT CommitId FROM ActiveCommit WHERE RepositoryId = @RepositoryId";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@RepositoryId", repositoryId);

            var result = await command.ExecuteScalarAsync();
            if (result != null && Guid.TryParse(result.ToString(), out var commitId))
            {
                return commitId;
            }

            return null;
        }

        public async Task<int> GetNextCommitNumber(int repositoryId)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT COALESCE(MAX(Number), 0) + 1 FROM Commits WHERE RepositoryId = @RepositoryId";
            using var command = new SQLiteCommand(sql, connection);
            command.Parameters.AddWithValue("@RepositoryId", repositoryId);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task ClearCommitTree(int repositoryId)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();
            try
            {
                var deleteActiveCommit = "DELETE FROM ActiveCommit WHERE RepositoryId = @RepositoryId";
                using var command1 = new SQLiteCommand(deleteActiveCommit, connection, transaction);
                command1.Parameters.AddWithValue("@RepositoryId", repositoryId);
                await command1.ExecuteNonQueryAsync();

                var deleteCommits = "DELETE FROM Commits WHERE RepositoryId = @RepositoryId";
                using var command2 = new SQLiteCommand(deleteCommits, connection, transaction);
                command2.Parameters.AddWithValue("@RepositoryId", repositoryId);
                await command2.ExecuteNonQueryAsync();

                var deleteRepository = "DELETE FROM Repositories WHERE Id = @RepositoryId";
                using var command3 = new SQLiteCommand(deleteRepository, connection, transaction);
                command3.Parameters.AddWithValue("@RepositoryId", repositoryId);
                await command3.ExecuteNonQueryAsync();

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
                Id = Guid.Parse(reader["Id"].ToString() ?? string.Empty),
                RepositoryId = Convert.ToInt32(reader["RepositoryId"]),
                Number = Convert.ToInt32(reader["Number"]),
                Hash = reader["Hash"].ToString() ?? string.Empty,
                Message = reader["Message"].ToString() ?? string.Empty,
                Timestamp = DateTime.Parse(reader["Timestamp"].ToString() ?? string.Empty),
                ParentId = parentId,
                BranchName = reader["BranchName"].ToString() ?? string.Empty
            };
        }

        public async Task<int> GetRepositoryId(string directoryPath)
        {
            using var connection = new SQLiteConnection(_connectionString);
            await connection.OpenAsync();

            var sql = "SELECT RepositoryId FROM Repositories WHERE Path = @directoryPath";
            using var command1 = new SQLiteCommand(sql, connection);
            command1.Parameters.AddWithValue("@directoryPath", directoryPath);

            var repositoryId = await command1.ExecuteScalarAsync();
            if (repositoryId != null)
            {
                return Convert.ToInt32(repositoryId);
            }

            sql = "INSERT INTO Repositories (Path) VALUES (@directoryPath) RETURNING Id";
            using var command2 = new SQLiteCommand(sql, connection);
            command2.Parameters.AddWithValue("@directoryPath", directoryPath);

            repositoryId = await command2.ExecuteScalarAsync();
            return Convert.ToInt32(repositoryId);
        }
    }
}