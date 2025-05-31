using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetEnv;


namespace GitUtility
{
    public class GitManager
    {
        private readonly DatabaseManager _dbManager;
        private readonly string _githubToken;
        private readonly string _githubUsername;
        private readonly string _gitignoreText;
        private readonly string _readmeText;
        private readonly HttpClient _httpClient;

        public GitManager()
        {
            string envPath = Path.Combine(AppContext.BaseDirectory, ".env");
            Env.Load(envPath);
            _githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                ?? throw new InvalidOperationException("GITHUB_TOKEN �� ������ � .env �����");
            _githubUsername = Environment.GetEnvironmentVariable("GITHUB_USERNAME")
                ?? throw new InvalidOperationException("GITHUB_USERNAME �� ������ � .env �����");
            string _gitignorePath = Environment.GetEnvironmentVariable("GITIGNORE_PATH")
                ?? throw new InvalidOperationException("GITIGNORE_PATH �� ������ � .env �����");
            string _readmePath = Environment.GetEnvironmentVariable("README_PATH")
                ?? throw new InvalidOperationException("README_PATH �� ������ � .env �����");

            _readmeText = ReadFile(_readmePath.Replace(@"\", "/"));
            _gitignoreText = ReadFile(_gitignorePath.Replace(@"\", "/"));

            _dbManager = new DatabaseManager();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"token {_githubToken}");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitUtility");
        }

        private string ReadFile(string path)
        {
            string text = string.Empty;
            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    text = reader.ReadToEnd();
                }
            }
            catch (IOException e)
            {
                Console.WriteLine("Error reading file: {0}", e.Message);
            }
            return text;
        }

        public async Task ExecuteCommand(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            var command = args[0].ToLower();
            var description = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "";

            switch (command)
            {
                case "init":
                    await InitCommand();
                    break;
                case "start":
                    await StartCommand();
                    break;
                case "fix":
                    await FixCommand(description);
                    break;
                case "prev":
                    await PrevCommand();
                    break;
                case "ch":
                    if (args.Length < 2 || !int.TryParse(args[1], out int commitNumber))
                    {
                        Console.WriteLine("������: ������� ����� ������� ��� ������� ch");
                        return;
                    }
                    await ChCommand(commitNumber);
                    break;
                case "show":
                    if (args.Length > 1 && int.TryParse(args[1], out int showCommitNumber))
                        await ShowCommand(showCommitNumber);
                    else
                        await ShowCommand();
                    break;
                case "save":
                    await SaveCommand();
                    break;
                default:
                    Console.WriteLine($"����������� �������: {command}");
                    ShowHelp();
                    break;
            }
        }

        private async Task InitCommand()
        {
            GitRepositoryValidator.EnsureGitRepositoryDoesNotExist();
            var currentDir = Directory.GetCurrentDirectory();
            var repoName = ToKebabCase(Path.GetFileName(currentDir));

            Console.WriteLine($"������������� ����������� '{repoName}'...");

            // ������������� ���������� Git �����������
            await RunGitCommand("init");
            await RunGitCommand("branch -M main");

            // �������� README.md ���� �����������
            var readmePath = Path.Combine(currentDir, "README.md");
            if (!File.Exists(readmePath))
            {
                await File.WriteAllTextAsync(readmePath, $"# {repoName}\n\n" + _readmeText);
                Console.WriteLine("������ README.md");
            }

            // �������� .gitignore ���� �����������
            var gitignorePath = Path.Combine(currentDir, ".gitignore");
            if (!File.Exists(gitignorePath))
            {
                await File.WriteAllTextAsync(gitignorePath, _gitignoreText);
                Console.WriteLine("������ .gitignore");
            }

            // ���������� � ������ ������
            await RunGitCommand("add .");
            await RunGitCommand("commit -m \"Initial commit\"");

            // �������� ���������� ����������� �� GitHub
            await CreateGitHubRepository(repoName);

            // ���������� � ��������� ������������
            var remoteUrl = $"https://github.com/{_githubUsername}/{repoName}.git";
            await RunGitCommand($"remote add origin {remoteUrl}");

            // Push � ��������� �����������
            await RunGitCommand("push -u origin main");

            Console.WriteLine($"����������� '{repoName}' ������� ������ � �������� �� GitHub!");
        }

        private async Task StartCommand()
        {
            GitRepositoryValidator.EnsureGitRepositoryExists();
            // ��������� ������� �������� ������
            var activeCommitId = await _dbManager.GetActiveCommitId();
            if (activeCommitId != null)
            {
                Console.WriteLine("������: �� ������� ������� ������! ��������� ������� 'save' ��� � ��������, ������ ��� ��������� �����.");
                return;
            }
            Console.WriteLine("������ ����� ������ ������...");

            // ����� ���� ����������������� ���������
            await RunGitCommand("reset --hard");
            await RunGitCommand("clean -fd");

            // Pull ��������� ���������
            await RunGitCommand("pull");

            // �������� ����� ����� ��� ������
            var branchName = $"work-{DateTime.Now:yyyyMMdd-HHmmss}";
            await RunGitCommand($"checkout -b {branchName}");

            // �������� ���������� ������� � ������
            var repoPath = Directory.GetCurrentDirectory();
            var currentCommitHash = await GetCurrentCommitHash();

            var rootCommit = new CommitNode
            {
                Id = Guid.NewGuid(),
                Number = 1,
                Hash = currentCommitHash,
                Message = "start",
                Timestamp = DateTime.Now,
                ParentId = null,
                BranchName = branchName
            };

            await _dbManager.ClearCommitTree();
            await _dbManager.SaveCommit(rootCommit);
            await _dbManager.SetActiveCommit(rootCommit.Id);

            Console.WriteLine($"������� ������� ����� '{branchName}' � ��������� ������");
        }

        private async Task FixCommand(string description = "")
        {
            GitRepositoryValidator.EnsureGitRepositoryExists();
            // ���������, ���� �� �������� ������ ��������
            var activeCommitId = await _dbManager.GetActiveCommitId();
            if (activeCommitId == null)
            {
                Console.WriteLine("��� �������� ������ ������. ��������� start...");
                await StartCommand();

                // ����� start ��������� ��� ���
                activeCommitId = await _dbManager.GetActiveCommitId();
                if (activeCommitId == null)
                {
                    Console.WriteLine("������: �� ������� ���������������� ������ ������");
                    return;
                }
            }

            // ��������� ������� ���������
            var status = await RunGitCommand("status --porcelain");
            if (string.IsNullOrWhiteSpace(status))
            {
                Console.WriteLine("��� ��������� ��� ��������");
                return;
            }

            var message = string.IsNullOrWhiteSpace(description) ? "fix" : description;

            // ��������� � �������� ���������
            await RunGitCommand("add .");
            await RunGitCommand($"commit -m \"{message}\"");

            var currentCommitHash = await GetCurrentCommitHash();
            activeCommitId = await _dbManager.GetActiveCommitId();
            var nextNumber = await _dbManager.GetNextCommitNumber();

            // ���������, ����� �� ������� ����� �����
            string branchName = await GetCurrentBranch();

            // ���� �� ������ fix ����� prev, ������� ����� �����
            if (activeCommitId.HasValue)
            {
                var childrenCount = await _dbManager.GetChildrenCount(activeCommitId.Value);

                if (childrenCount > 0)
                {
                    // ������� ����� ����� ��� �����������
                    branchName = $"branch-{DateTime.Now:HHmmss}-{nextNumber}";
                    await RunGitCommand($"checkout -b {branchName}");

                    // ���������� ������ � ����� �����
                    await RunGitCommand($"cherry-pick {currentCommitHash}");
                    currentCommitHash = await GetCurrentCommitHash();
                }
            }

            var newCommit = new CommitNode
            {
                Id = Guid.NewGuid(),
                Number = nextNumber,
                Hash = currentCommitHash,
                Message = message,
                Timestamp = DateTime.Now,
                ParentId = activeCommitId,
                BranchName = branchName
            };

            await _dbManager.SaveCommit(newCommit);
            await _dbManager.SetActiveCommit(newCommit.Id);

            Console.WriteLine($"������ #{nextNumber} ������: {message}");
        }

        private async Task PrevCommand()
        {
            GitRepositoryValidator.EnsureGitRepositoryExists();
            var activeCommitId = await _dbManager.GetActiveCommitId();
            if (activeCommitId == null)
            {
                Console.WriteLine("������: ��� �������� ������ ������. ��������� ������� 'start' �������.");
                return;
            }

            var activeCommit = await _dbManager.GetCommit(activeCommitId.Value);
            if (activeCommit?.ParentId == null)
            {
                Console.WriteLine("������: ��� ������ ������ ����� start");
                return;
            }

            var parentCommit = await _dbManager.GetCommit(activeCommit.ParentId.Value);

            await RunGitCommand($"reset --hard {parentCommit?.Hash}");
            await _dbManager.SetActiveCommit(parentCommit?.Id ?? Guid.Empty);

            Console.WriteLine($"����� � ������� #{parentCommit?.Number}: {parentCommit?.Message}");
        }

        private async Task ChCommand(int commitNumber)
        {
            GitRepositoryValidator.EnsureGitRepositoryExists();
            // ��������� ������� �������� ������
            var activeCommitId = await _dbManager.GetActiveCommitId();
            if (activeCommitId == null)
            {
                Console.WriteLine("��� �������� ������ ������. ��������� ������� 'start' ��� ������ ������.");
                return;
            }

            // ������� ��������� fix
            var status = await RunGitCommand("status --porcelain");
            if (!string.IsNullOrWhiteSpace(status))
            {
                await FixCommand();
            }

            var targetCommit = await _dbManager.GetCommitByNumber(commitNumber);
            if (targetCommit == null)
            {
                Console.WriteLine($"������: ������ #{commitNumber} �� ������");
                return;
            }

            await RunGitCommand($"reset --hard {targetCommit.Hash}");
            await _dbManager.SetActiveCommit(targetCommit.Id);

            Console.WriteLine($"������� � ������� #{targetCommit.Number}: {targetCommit.Message}");
        }

        private async Task ShowCommand(int? specificCommit = null)
        {
            GitRepositoryValidator.EnsureGitRepositoryExists();
            // ��������� ������� �������� ������
            var activeCommitId = await _dbManager.GetActiveCommitId();
            if (activeCommitId == null)
            {
                Console.WriteLine("��� �������� ������ ������. ��������� ������� 'start' ��� ������ ������.");
                return;
            }
            if (specificCommit.HasValue)
            {
                var commit = await _dbManager.GetCommitByNumber(specificCommit.Value);
                if (commit == null)
                {
                    Console.WriteLine($"������ #{specificCommit} �� ������");
                    return;
                }
                Console.WriteLine($"������ #{commit.Number}:");
                Console.WriteLine($"  �����: {commit.Timestamp:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  ���������: {commit.Message}");
                return;
            }

            var commits = await _dbManager.GetAllCommits();

            if (!commits.Any())
            {
                Console.WriteLine("��� �������� � ������");
                return;
            }

            // ���������� ������ ��������
            var tree = BuildCommitTree(commits, activeCommitId);
            Console.WriteLine(tree);
        }

        private async Task SaveCommand()
        {
            GitRepositoryValidator.EnsureGitRepositoryExists();
            // ��������� ������� �������� ������
            var activeCommitId = await _dbManager.GetActiveCommitId();
            if (activeCommitId == null)
            {
                Console.WriteLine("������: ��� �������� ������ ������. ��������� ������� 'start' �������.");
                return;
            }
            Console.WriteLine("���������� ���������...");

            // ��������� fix ������ ���� ���� ���������
            var status = await RunGitCommand("status --porcelain");
            if (!string.IsNullOrWhiteSpace(status))
            {
                await FixCommand();
            }

            // Push ���������
            var currentBranch = await GetCurrentBranch();
            await RunGitCommand($"push -u origin {currentBranch}");

            // �������� Pull Request
            await CreatePullRequest(currentBranch);

            // ������� �� �������� �����
            await RunGitCommand("checkout main");

            // �������� ��������� ������� �����
            await RunGitCommand($"branch -D {currentBranch}");

            // ������� ������ ��������
            await _dbManager.ClearCommitTree();

            Console.WriteLine("��������� ���������, Pull Request ������, ������ �������");
        }

        private string BuildCommitTree(List<CommitNode> commits, Guid? activeCommitId)
        {
            var sb = new StringBuilder();
            var commitDict = commits.ToDictionary(c => c.Id);
            var root = commits.First(c => c.ParentId == null);

            BuildTreeRecursive(sb, commitDict, root, "", true, activeCommitId);
            return sb.ToString();
        }

        private void BuildTreeRecursive(StringBuilder sb, Dictionary<Guid, CommitNode> commits,
            CommitNode current, string prefix, bool isRoot, Guid? activeCommitId)
        {
            var isActive = current.Id == activeCommitId;
            var marker = isActive ? " <- active" : "";

            if (isRoot)
            {
                sb.AppendLine($"start - {current.Number}{marker}");
            }
            else
            {
                sb.AppendLine($"{prefix}{current.Number}{marker}");
            }

            var children = commits.Values.Where(c => c.ParentId == current.Id).OrderBy(c => c.Number).ToList();

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var isLast = i == children.Count - 1;
                var newPrefix = isRoot ? (isLast ? "     + - " : "     | - ")
                                       : prefix.Replace("+ - ", "    ").Replace("| - ", "|   ") + (isLast ? "+ - " : "| - ");

                BuildTreeRecursive(sb, commits, child, newPrefix, false, activeCommitId);
            }
        }

        private async Task CreateGitHubRepository(string repoName)
        {
            var createRepoData = new
            {
                name = repoName,
                description = $"Repository for {repoName}",
                @private = false,
                auto_init = false
            };

            var json = JsonSerializer.Serialize(createRepoData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.github.com/user/repos", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"�� ������� ������� ����������� �� GitHub: {error}");
            }

            Console.WriteLine($"��������� ����������� '{repoName}' ������ �� GitHub");
        }

        private async Task CreatePullRequest(string branchName)
        {
            var repoName = ToKebabCase(Path.GetFileName(Directory.GetCurrentDirectory()));

            var prData = new
            {
                title = $"Work branch: {branchName}",
                head = branchName,
                @base = "main",
                body = "Automatically created pull request from work branch"
            };

            var json = JsonSerializer.Serialize(prData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"https://api.github.com/repos/{_githubUsername}/{repoName}/pulls", content);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("Pull Request ������ �������");
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"��������������: �� ������� ������� Pull Request: {error}");
            }
        }

        private async Task<string> RunGitCommand(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                if (process.ExitCode != 0)
                {
                    throw new Exception($"Git command failed: {error}");
                }

                return output.Trim();
            }
            return string.Empty;  
        }

        private async Task<string> GetCurrentCommitHash()
        {
            return await RunGitCommand("rev-parse HEAD");
        }

        private async Task<string> GetCurrentBranch()
        {
            return await RunGitCommand("branch --show-current");
        }

        private string ToKebabCase(string input)
        {
            return Regex.Replace(input, @"[\s_]", "-")
                       .ToLowerInvariant()
                       .Trim('-');
        }


        private void ShowHelp()
        {
            Console.WriteLine("Git Utility - ������� ��� ������ � Git");
            Console.WriteLine();
            Console.WriteLine("�������:");
            Console.WriteLine("  init                 - ������������� ������ �����������");
            Console.WriteLine("  start                - ������ ����� ������ ������");
            Console.WriteLine("  fix [��������]       - �������� ��������� � ������");
            Console.WriteLine("  prev                 - ����� � ����������� �������");
            Console.WriteLine("  ch <�����>           - ������� � ������� �� ������");
            Console.WriteLine("  show [�����]         - �������� ������ �������� ��� ������ �������");
            Console.WriteLine("  save                 - ���������� � �������� ���������");
        }
    }
}