using Moq;
using PaperlessREST.BL;
using PaperlessREST.Tests.Utils;

namespace PaperlessREST.Tests;

[TestFixture]
public sealed class DocumentSearchServiceTests
{
    private readonly CancellationToken _ct = CancellationToken.None;

    private MockRepository _mocks = null!;
    private Mock<IDocumentSearchService> _searchService = null!;

    [SetUp]
    public void SetUp()
    {
        _mocks = new MockRepository(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
        _searchService = _mocks.Create<IDocumentSearchService>();
    }

    [TearDown]
    public void TearDown()
    {
        _mocks.VerifyAll();
        _mocks.VerifyNoOtherCalls();
    }

    [Test]
    public async Task SearchAsync_ReturnsExpectedDocuments()
    {
        const string query = "invoice";
        const int limit = 10;
        var doc1 = new DocumentBuilder().Build();
        var doc2 = new DocumentBuilder().Build();

        _searchService.Setup(s => s.SearchAsync<Document>(query, limit, _ct))
            .Returns(new[] { doc1, doc2 }.ToAsyncEnumerable());

        var results = new List<Document>();
        await foreach (var doc in _searchService.Object.SearchAsync<Document>(query, limit, _ct))
        {
            results.Add(doc);
        }

        Assert.That(results, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results[0], Is.SameAs(doc1));
            Assert.That(results[1], Is.SameAs(doc2));
        }
    }

    [Test]
    public async Task SearchAsync_WithEmptyResults_ReturnsEmptySequence()
    {
        const string query = "nonexistent";
        const int limit = 5;

        _searchService.Setup(s => s.SearchAsync<Document>(query, limit, _ct))
            .Returns(Array.Empty<Document>().ToAsyncEnumerable());

        var results = new List<Document>();
        await foreach (var doc in _searchService.Object.SearchAsync<Document>(query, limit, _ct))
        {
            results.Add(doc);
        }

        Assert.That(results, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_ReturnsTrue_WhenSuccessful()
    {
        var id = Guid.NewGuid();
        _searchService.Setup(s => s.DeleteAsync(id, _ct)).ReturnsAsync(true);

        var result = await _searchService.Object.DeleteAsync(id, _ct);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DeleteAsync_ReturnsFalse_WhenUnsuccessful()
    {
        var id = Guid.NewGuid();
        _searchService.Setup(s => s.DeleteAsync(id, _ct)).ReturnsAsync(false);

        var result = await _searchService.Object.DeleteAsync(id, _ct);

        Assert.That(result, Is.False);
    }
}