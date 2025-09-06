using DotNet.Testcontainers.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Testcontainers.PostgreSql;
using Xunit;

namespace PaperlessREST.IntegrationTests;

public record Document(Guid Id, string FileName, string StoragePath);

public class DocumentDbContext(DbContextOptions<DocumentDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents { get; init; } = null!;
}

public interface IDocumentService
{
    Task<Document> UploadDocumentAsync(string fileName, CancellationToken ct = default);
}

public class DocumentServiceExample(
    IDbContextFactory<DocumentDbContext> dbContextFactory,
    ILogger<DocumentServiceExample>? logger = null) : IDocumentService
{
    private readonly ILogger<DocumentServiceExample> _logger = logger ?? NullLogger<DocumentServiceExample>.Instance;

    public async Task<Document> UploadDocumentAsync(string fileName, CancellationToken ct = default)
    {
        var document = new Document(Guid.NewGuid(), fileName, $"{Guid.NewGuid()}/{fileName}");
        _logger.LogInformation("Uploading document {FileName}", fileName);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Document {Id} persisted", document.Id);
        return document;
    }
}

public sealed class PaperlessFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder().WithLogger(new FakeLogger())
        .WithWaitStrategy(
            Wait.ForUnixContainer().UntilMessageIsLogged("database system is ready to accept connections")).Build();

    public IServiceProvider Services { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var basePath = FindProjectDirectory();

        await _dbContainer.StartAsync();
        var conn = _dbContainer.GetConnectionString();

        var config = new ConfigurationBuilder().SetBasePath(basePath).AddJsonFile("appsettings.json", false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PaperlessDb"] = conn
            }).Build();

        Services = new ServiceCollection().AddSingleton(config)
            .AddDbContextFactory<DocumentDbContext>(o => o.UseNpgsql(config.GetConnectionString("PaperlessDb")))
            .AddLogging().AddSingleton<IDocumentService, DocumentServiceExample>().BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContainer.DisposeAsync().AsTask();
    }

    private static string FindProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && directory.GetFiles("*.csproj").Length == 0) directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Project root not found.");
    }
}

public sealed class DocumentServiceTests : IClassFixture<PaperlessFixture>, IAsyncLifetime
{
    private readonly IServiceProvider _services;
    private readonly ITestOutputHelper _output;
    private DocumentDbContext _dbContext = null!;

    private IDocumentService _documentService = null!;
    private AsyncServiceScope _scope;

    public DocumentServiceTests(PaperlessFixture fixture, ITestOutputHelper output)
    {
        _services = fixture.Services;
        _output = output;
    }

    public ValueTask InitializeAsync()
    {
        _scope = _services.CreateAsyncScope();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddXUnit(_output);
            builder.SetMinimumLevel(LogLevel.Trace);
        });

        var dbFactory = _scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentDbContext>>();
        var logger = loggerFactory.CreateLogger<DocumentServiceExample>();
        _documentService = new DocumentServiceExample(dbFactory, logger);

        _dbContext = _scope.ServiceProvider.GetRequiredService<DocumentDbContext>();

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return _scope.DisposeAsync();
    }

    [Fact]
    public async Task UploadDocumentAsync_PersistsDocument_ToDatabase()
    {
        const string fileName = "modern-test.pdf";

        var createdDocument =
            await _documentService.UploadDocumentAsync(fileName, TestContext.Current.CancellationToken);

        var dbDocument = await _dbContext.Documents.FindAsync([createdDocument.Id],
            TestContext.Current.CancellationToken);
        Assert.NotNull(dbDocument);
        Assert.Equal(fileName, dbDocument.FileName);
    }
}