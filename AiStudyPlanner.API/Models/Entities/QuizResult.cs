using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AiStudyPlanner.API.Models.Entities;

public class QuizResult
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

    // ─── Attempt Info ──────────────────────────────────────────────
    public int AttemptNumber { get; set; } = 1;
    // increments each time student retakes this topic's quiz

    public QuizType QuizType { get; set; } = QuizType.TopicEnd;
    // when was this quiz taken?

    // ─── Score ─────────────────────────────────────────────────────
    [Required]
    public int TotalQuestions { get; set; }

    public int CorrectAnswers { get; set; }

    public int WrongAnswers { get; set; }

    public int SkippedQuestions { get; set; }

    [Column(TypeName = "REAL")]
    public double ScorePercent { get; set; }
    // computed: (CorrectAnswers / TotalQuestions) * 100

    // ─── Time Tracking ─────────────────────────────────────────────
    public int TimeTakenSeconds { get; set; }
    // how long the student took to complete the quiz

    public int AverageSecondsPerQuestion => TotalQuestions > 0 ? TimeTakenSeconds / TotalQuestions : 0;

    // ─── Answer Breakdown (JSON) ───────────────────────────────────
    public string? AnswerBreakdownJson { get; set; }
    // stores per-question detail as JSON array:
    // [{ questionId, questionText, selectedOption,
    //    correctOption, isCorrect, timeTakenSeconds }]
    // used by "Review Answers" screen and weak area detection

    // ─── AI Feedback ───────────────────────────────────────────────
    public string? AiFeedback { get; set; }
    // Gemini-generated feedback after quiz:
    // "You struggled with Newton's 3rd law. Focus on action-reaction pairs."

    public string? SuggestedRevisionTopics { get; set; }
    // comma-separated topic names Gemini flags for revision

    // ─── Outcome Flags ─────────────────────────────────────────────
    public bool IsPassed { get; set; }
    // true if ScorePercent >= passing threshold (default 60%)

    public bool TriggeredWeakAreaFlag { get; set; } = false;
    // true if this result caused ProgressService to flag IsWeakArea

    public bool TriggeredRevisionSlot { get; set; } = false;
    // true if this result caused a revision slot to be added to the plan

    // ─── Timestamps ────────────────────────────────────────────────
    public DateTime TakenAt { get; set; } = DateTime.UtcNow;

    // ─── Computed Helper (not mapped to DB) ────────────────────────
    [NotMapped]
    public bool IsStrugglingResult => ScorePercent < 40.0;
    // used in ProgressService to decide severity of weak area
}

// ─── Enums ─────────────────────────────────────────────────────────
public enum QuizType
{
    TopicEnd   = 0,   // taken right after studying a topic
    DailyEnd   = 1,   // end-of-day recap quiz across all day's topics
    Revision   = 2,   // retake after weak area is flagged
    MockExam   = 3    // full-length timed exam simulation
}