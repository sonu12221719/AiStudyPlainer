using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStudyPlanner.API.Models.Entities;

public class StudyPlan
{
    [Key]
    public int Id { get; set; }
    [Required]
    public int UserId { get; set; }
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;
    [MaxLength(50)]
    public string ExamTarget { get; set; } = string.Empty;
    public PlanSource Source { get; set; } = PlanSource.SyllabusUpload;
    [MaxLength(500)]
    public string? SyllabusFileName { get; set; }
    public string? RawSyllabusText { get; set; }
    [Required]
    public DateTime StartDate { get; set; }
    [Required]
    public DateTime EndDate { get; set; }
    public int TotalDays =>
        (int)(EndDate - StartDate).TotalDays + 1;
    public string? GeneratedPlanJson { get; set; }
    public PlanStatus Status { get; set; } = PlanStatus.Active;
    public bool IsVectorIndexed { get; set; } = false;
    [Column(TypeName = "REAL")]
    public double CompletionPercent { get; set; } = 0.0;  
    public int TotalTopics { get; set; } = 0;

    public int CompletedTopics { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public ICollection<Topic> Topics { get; set; } = new List<Topic>();
}

public enum PlanSource
{
    SyllabusUpload = 0, ExamPreset     = 1
}

public enum PlanStatus
{
    Active    = 0,
    Paused    = 1,
    Completed = 2,
    Archived  = 3
}