using System;

namespace AiStudyPlanner.API.Utilities;

public static class PasswordHelper
{
    public static string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    public static bool Verify(string password, string hashedPassword)
    {
        return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
    }

    public static PasswordStrengthResult CheckStrength(string password)
    {
        var errors = new List<string>();

        if (password.Length < 8)
            errors.Add("At least 8 characters required.");

        if (!password.Any(char.IsUpper))
            errors.Add("At least one uppercase letter required.");

        if (!password.Any(char.IsLower))
            errors.Add("At least one lowercase letter required.");

        if (!password.Any(char.IsDigit))
            errors.Add("At least one number required.");

        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            errors.Add("At least one special character required.");

        return new PasswordStrengthResult
        {
            IsStrong = errors.Count == 0,
            Errors   = errors
        };
    }

    public class PasswordStrengthResult
    {
        public bool         IsStrong { get; set; }
        public List<string> Errors   { get; set; } = new();
    }
}
