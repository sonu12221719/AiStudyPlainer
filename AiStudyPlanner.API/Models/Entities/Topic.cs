using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStudyPlanner.API.Models.Entities;

public class Topic
{
    [Key]
    public int Id { get; set; }
    [Required]
    public int StudyPlanId { get; set; }
    [ForeignKey(nameof(StudyPlanId))]
    public StudyPlan StudyPlan { get; set; } = null!;
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
    [MaxLength(100)]
    public string Subject { get; set; } = string.Empty;
    [MaxLength(100)]
    public string? Chapter { get; set; }
    public string? Description { get; set; }
    public int DayNumber { get; set; }
    [Required]
    public DateTime ScheduledDate { get; set; }
    public int EstimatedMinutes { get; set; } = 60;
    public int DifficultyLevel { get; set; } = 2;
    public TopicStatus Status { get; set; } = TopicStatus.Pending;
    public DateTime? CompletedAt { get; set; }
    public int ActualMinutesSpent { get; set; } = 0;
    [Column(TypeName = "REAL")]
    public double ScorePercent { get; set; } = 0.0;
    public int QuizAttempts { get; set; } = 0;
    public bool IsWeakArea { get; set; } = false;
    public bool IsRevisionScheduled { get; set; } = false;
    public int PriorityOrder { get; set; } = 0;
    public bool IsExamCritical { get; set; } = false;
    [MaxLength(100)]
    public string? QdrantTopicTag { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
    public ICollection<QuizResult> QuizResults { get; set; } = new List<QuizResult>();
    public ICollection<WeakArea> WeakAreas { get; set; } = new List<WeakArea>();
}

public enum TopicStatus
{
    Pending    = 0,
    InProgress = 1,
    Completed  = 2,
    Skipped    = 3,
    Revised    = 4 
}