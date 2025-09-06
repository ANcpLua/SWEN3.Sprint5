using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace PaperlessREST;

public record DocumentDto
{
    [Description("Unique document identifier")]
    public Guid Id { get; init; }

    [Description("Original PDF filename")]
    public string FileName { get; init; } = null!;

    [Description("Processing status")]
    public string Status { get; init; } = null!;

    [Description("Upload timestamp")]
    public DateTimeOffset CreatedAt { get; init; }

    [Description("Storage path in MinIO")]
    public string StoragePath { get; init; } = null!;

    [Description("OCR extracted text content")]
    public string? Content { get; init; }

    [Description("Processing completion timestamp")]
    public DateTimeOffset? ProcessedAt { get; init; }
}

public record CreateDocumentResponse
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = null!;
    public string Status { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
}

public record SearchQuery
{
    [Required(ErrorMessage = "Search query is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Search query must be between 1 and 100 characters")]
    public required string Query { get; init; }

    [Range(1, 100, ErrorMessage = "Limit must be between 1 and 100")]
    public int Limit { get; init; } = 10;
}