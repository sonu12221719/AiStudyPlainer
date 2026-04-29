using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStudyPlanner.API.Models.Entities;

public class User
{
    [Key]
    public int Id { get; set; }
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    [Required]
    [MaxLength(255)]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
    [MaxLength(100)]
    public string? ExamTarget { get; set; }
    public DateTime? ExamDate { get; set; }
    [MaxLength(50)]
    public string PreferredLanguage { get; set; } = "English";
    public int DailyStudyHours { get; set; } = 4;
    public bool IsActive { get; set; } = true;
    [MaxLength(500)]
    public string? ProfilePictureUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ICollection<StudyPlan> StudyPlans { get; set; } = new List<StudyPlan>();
    public ICollection<QuizResult> QuizResults { get; set; } = new List<QuizResult>();
    public ICollection<WeakArea> WeakAreas { get; set; } = new List<WeakArea>();
    public ICollection<SyllabusChunk> SyllabusChunks { get; set; } = new List<SyllabusChunk>();
}

