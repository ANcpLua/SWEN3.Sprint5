using System.ComponentModel.DataAnnotations;

namespace SWEN3.Sprint5.GenAI;

/// <summary>
///     Options for configuring the Google Gemini summarization API.
/// </summary>
public sealed class GeminiOptions
{
    [Required(ErrorMessage = "Gemini API key is required")]
    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "gemini-2.0-flash";

    [Range(1, 10, ErrorMessage = "MaxRetries must be between 1 and 10")]
    public int MaxRetries { get; init; } = 3;

    [Range(5, 120, ErrorMessage = "TimeoutSeconds must be between 5 and 120")]
    public int TimeoutSeconds { get; init; } = 30;
}