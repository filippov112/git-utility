using System;
using System.IO;

namespace GitUtility
{
    public static class GitRepositoryValidator
    {
        public static bool IsGitRepository(string path = ".")
        {
            return Directory.Exists(Path.Combine(path, ".git"));
        }

        public static void EnsureGitRepositoryExists(string path = ".")
        {
            if (!IsGitRepository(path))
            {
                throw new InvalidOperationException("Текущая директория не является Git-репозиторием. Используйте команду 'init' для инициализации.");
            }
        }

        public static void EnsureGitRepositoryDoesNotExist(string path = ".")
        {
            if (IsGitRepository(path))
            {
                throw new InvalidOperationException("Git-репозиторий уже существует в текущей директории.");
            }
        }
    }
}