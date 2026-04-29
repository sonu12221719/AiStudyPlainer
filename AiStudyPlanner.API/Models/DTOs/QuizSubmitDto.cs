using System;
using System.ComponentModel.DataAnnotations;

namespace AiStudyPlanner.API.Models.DTOs;

public class QuizSubmitDto
{
    [Required]
    public int TopicId { get; set; }

    public string QuizType { get; set; } = "TopicEnd";
    // "TopicEnd" | "DailyEnd" | "Revision" | "MockExam"

    [Required]
    [Range(1, 100)]
    public int TotalQuestions { get; set; }

    [Required]
    public int CorrectAnswers { get; set; }

    public int WrongAnswers { get; set; }

    public int SkippedQuestions { get; set; }

    [Required]
    public int TimeTakenSeconds { get; set; }

    public List<QuizAnswerDetailDto> Answers { get; set; } = new();
    // per-question breakdown — used for AiFeedback and weak area detection
}
