using JetBrains.Annotations;

namespace PaperlessREST;

public class Document
{
    public Document()
    {
    }

    internal Document(Guid id, string fileName, DocumentStatus status, DateTimeOffset createdAt, string storagePath,
        string? content = null, DateTimeOffset? processedAt = null)
    {
        Id = id;
        FileName = fileName;
        Status = status;
        CreatedAt = createdAt;
        StoragePath = storagePath;
        Content = content;
        ProcessedAt = processedAt;
    }

    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public DocumentStatus Status { get; set; }
    [UsedImplicitly] public DateTimeOffset CreatedAt { get; set; }
    public string StoragePath { get; set; } = null!;
    [UsedImplicitly] public string? Content { get; set; }
    [UsedImplicitly] public DateTimeOffset? ProcessedAt { get; set; }
    [UsedImplicitly] public string? Summary { get; internal set; }
    [UsedImplicitly] public DateTimeOffset? SummaryGeneratedAt { get; internal set; }

    public static Document CreateFromUpload(string fileName)
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        return new Document
        {
            Id = id,
            FileName = fileName,
            Status = DocumentStatus.Pending,
            CreatedAt = createdAt,
            StoragePath = $"documents/{createdAt:yyyy-MM}/{id}.pdf"
        };
    }

    public void MarkAsCompleted(string content)
    {
        if (Status is not DocumentStatus.Pending)
            throw new InvalidOperationException($"Cannot complete document in {Status} status");

        Status = DocumentStatus.Completed;
        Content = content;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsFailed()
    {
        if (Status is not DocumentStatus.Pending)
            throw new InvalidOperationException($"Cannot fail document in {Status} status");

        Status = DocumentStatus.Failed;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateSummary(string summary, DateTimeOffset generatedAt)
    {
        Summary = summary;
        SummaryGeneratedAt = generatedAt;
    }
}

public enum DocumentStatus
{
    Pending,
    Completed,
    Failed
}