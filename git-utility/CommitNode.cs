using System;

namespace GitUtility
{
    public class CommitNode
    {
        public Guid Id { get; set; }
        public int Number { get; set; }
        public string Hash { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public Guid? ParentId { get; set; }
        public string BranchName { get; set; }
    }
}
