using System;

namespace GitUtility
{
    public class CommitNode
    {
        public Guid Id { get; set; }
        public int RepositoryId { get; set; }
        public int Number { get; set; }
        public string Hash { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Guid? ParentId { get; set; }
        public string BranchName { get; set; } = string.Empty;
    }
}
