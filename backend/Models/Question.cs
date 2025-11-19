using System;
using System.Collections.Generic;

namespace RhcsaExamApi.Models
{
    public class Question
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Instructions { get; set; }
        public bool VmRequired { get; set; }
        public string ExpectedOutcome { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<QuestionProgress> Progress { get; set; }
    }

    public class QuestionProgress
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public string UserId { get; set; }
        public string Status { get; set; } // pending, completed, come-back-later
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public Question Question { get; set; }
    }

    public class VmInstance
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int QuestionId { get; set; }
        public string Status { get; set; } // running, stopped
        public string IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
