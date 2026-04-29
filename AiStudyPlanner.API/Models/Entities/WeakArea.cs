using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStudyPlanner.API.Models.Entities;

public class WeakArea
{
    // ─── Primary Key ───────────────────────────────────────────────
    [Key]
    public int Id { get; set; }

    // ─── Foreign Keys ──────────────────────────────────────────────
    [Required]
    public int UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [Required]
    public int TopicId { get; set; }

    [ForeignKey(nameof(TopicId))]
    public Topic Topic { get; set; } = null!;

    // ─── Weak Area Identity ────────────────────────────────────────
    [Required]
    [MaxLength(200)]
    public string TopicName { get; set; } = string.Empty;
    // denormalised from Topic.Name — avoids join on dashboard queries

    [MaxLength(100)]
    public string Subject { get; set; } = string.Empty;
    // denormalised from Topic.Subject — for grouping on heatmap

    // ─── Severity ──────────────────────────────────────────────────
    public WeakAreaSeverity Severity { get; set; } = WeakAreaSeverity.Moderate;
    // computed from ScorePercent:
    //   Critical  → score < 40%
    //   Moderate  → score 40–60%
    //   Improving → score 60–75% but still being watched

    [Column(TypeName = "REAL")]
    public double ScoreAtDetection { get; set; }
    // the ScorePercent that triggered this weak area record

    [Column(TypeName = "REAL")]
    public double CurrentScore { get; set; }
    // updated after every revision attempt — tracks improvement

    [Column(TypeName = "REAL")]
    public double? TargetScore { get; set; } = 75.0;
    // resolved when CurrentScore >= TargetScore

    // ─── Detection Context ─────────────────────────────────────────
    public int QuizResultId { get; set; }
    // the specific QuizResult that triggered this weak area

    public int AttemptNumberAtDetection { get; set; }
    // which attempt number caused the flag

    public int TotalFailedAttempts { get; set; } = 1;
    // increments each time student retakes and still scores below threshold

    // ─── Resolution Status ─────────────────────────────────────────
    public WeakAreaStatus Status { get; set; } = WeakAreaStatus.Active;

    public DateTime? ResolvedAt { get; set; }
    // set when CurrentScore >= TargetScore

    public string? ResolvedNote { get; set; }
    // e.g. "Scored 82% on revision attempt 3"

    // ─── AI Insight ────────────────────────────────────────────────
    public string? AiInsight { get; set; }
    // Gemini-generated diagnosis:
    // "Student consistently misses questions on
    //  action-reaction pairs and momentum conservation."

    public string? AiRecommendation { get; set; }
    // Gemini-generated action:
    // "Re-read Chapter 3 sections 3.4–3.6.
    //  Practice 10 numerical problems on momentum."

    // ─── Revision Tracking ─────────────────────────────────────────
    public int RevisionSlotsAdded { get; set; } = 0;
    // how many revision topics were inserted into the plan

    public int RevisionSlotsCompleted { get; set; } = 0;
    // how many of those revision slots the student actually finished

    public DateTime? LastRevisionAt { get; set; }
    // when the student last attempted a revision for this topic

    // ─── Heatmap Display ───────────────────────────────────────────
    public int HeatmapIntensity { get; set; } = 3;
    // 1 (light) to 5 (darkest red) — drives heatmap cell color in Angular
    // computed from Severity + TotalFailedAttempts

    // ─── Timestamps ────────────────────────────────────────────────
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // ─── Computed Helpers (not mapped to DB) ───────────────────────
    [NotMapped]
    public double ImprovementDelta => CurrentScore - ScoreAtDetection;
    // positive = improving, negative = getting worse

    [NotMapped]
    public bool IsResolved =>
        Status == WeakAreaStatus.Resolved;

    [NotMapped]
    public double ProgressToTarget =>
        TargetScore.HasValue && TargetScore > ScoreAtDetection
            ? Math.Clamp(
                (CurrentScore - ScoreAtDetection) /
                (TargetScore.Value - ScoreAtDetection) * 100, 0, 100)
            : 0;
    // percentage of the way from detection score to target score
    // used by the progress bar on the Weak Areas dashboard panel
}

// ─── Enums ─────────────────────────────────────────────────────────
public enum WeakAreaSeverity
{
    Improving = 0,   // score 60–75%, being monitored
    Moderate  = 1,   // score 40–60%, revision scheduled
    Critical  = 2    // score < 40%, urgent revision added next day
}

public enum WeakAreaStatus
{
    Active    = 0,   // still a problem
    Resolving = 1,   // revision in progress, score improving
    Resolved  = 2,   // hit target score — archived but kept for history
    Ignored   = 3    // student dismissed it manually
}