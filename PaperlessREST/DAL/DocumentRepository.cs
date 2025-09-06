using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace PaperlessREST.DAL;

public interface IDocumentRepository
{
    IAsyncEnumerable<Document> GetRecentDocumentsAsync(int limit, CancellationToken cancellationToken = default);

    ValueTask<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Document?> UpdateAsync(Document document, CancellationToken cancellationToken = default);
    Task<bool> FileNameExistsAsync(string fileName, CancellationToken cancellationToken = default);
}

public class DocumentRepository(
    IDbContextFactory<DocumentPersistence> contextFactory,
    ILogger<DocumentRepository> logger) : IDocumentRepository
{
    public async IAsyncEnumerable<Document> GetRecentDocumentsAsync(int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = db.Documents.OrderByDescending(d => d.CreatedAt).Take(limit).AsAsyncEnumerable();

        await foreach (var entity in entities.WithCancellation(cancellationToken)) yield return entity.ToDocument();
    }

    public async ValueTask<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Documents.FindAsync([id], cancellationToken);
        return entity?.ToDocument();
    }

    public async Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = document.ToDocumentEntity();
        db.Documents.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Document {DocumentId} persisted to database", entity.Id);
        return entity.ToDocument();
    }

    public async Task<Document?> UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = document.ToDocumentEntity();
        db.Documents.Update(entity);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Document {DocumentId} updated in database", entity.Id);
        return entity.ToDocument();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Documents.FindAsync([id], cancellationToken);
        if (entity is null) return false;

        db.Documents.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Document {DocumentId} removed from database", id);
        return true;
    }

    public async Task<bool> FileNameExistsAsync(string fileName, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Documents.AnyAsync(d => d.FileName == fileName, cancellationToken);
    }
}