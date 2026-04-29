using System;
using System.ComponentModel.DataAnnotations;

namespace AiStudyPlanner.API.Models.DTOs;

public class QuizAnswerDetailDto
{
    public int QuestionNumber { get; set; }

    [MaxLength(500)]
    public string QuestionText { get; set; } = string.Empty;

    [MaxLength(200)]
    public string SelectedOption { get; set; } = string.Empty;

    [MaxLength(200)]
    public string CorrectOption { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }

    public int TimeTakenSeconds { get; set; }
}
