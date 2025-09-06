using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Response;
using Moq;
using PaperlessREST.BL;

namespace PaperlessREST.Tests;

[TestFixture]
public class DocumentStorageServiceTests
{
    [SetUp]
    public void Setup()
    {
        _minio = new Mock<IMinioClient>();
        _options = new Mock<IOptions<MinioOptions>>();
        _options.Setup(o => o.Value).Returns(new MinioOptions
            { Endpoint = "http://minio:9000", AccessKey = "a", SecretKey = "s", BucketName = "test-bucket" });
        _logger = LoggerFactory.Create(_ => { }).CreateLogger<DocumentStorageService>();
        _sut = new DocumentStorageService(_minio.Object, _options.Object, _logger);
    }

    private Mock<IMinioClient> _minio = null!;
    private Mock<IOptions<MinioOptions>> _options = null!;
    private ILogger<DocumentStorageService> _logger = null!;
    private DocumentStorageService _sut = null!;
    private readonly CancellationToken _ct = CancellationToken.None;

    [Test]
    public async Task DeleteAsync_WhenSuccessful_ReturnsTrue()
    {
        const string storagePath = "docs/test.pdf";
        _minio.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), _ct)).Returns(Task.CompletedTask);
        var result = await _sut.DeleteAsync(storagePath, _ct);
        Assert.That(result, Is.True);
    }

    [Test]
    public async Task DeleteAsync_WhenMinioThrows_ReturnsFalse()
    {
        const string storagePath = "docs/fail.pdf";
        _minio.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), _ct))
            .ThrowsAsync(new Exception("Minio delete error"));
        var result = await _sut.DeleteAsync(storagePath, _ct);
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task UploadAsync_WhenSuccessful_Completes()
    {
        var stream = new MemoryStream([1, 2, 3]);
        const string path = "docs/upload.pdf";
        const long size = 3L;
        _minio.Setup(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), _ct)).ReturnsAsync(
            new PutObjectResponse(HttpStatusCode.OK, string.Empty, new Dictionary<string, string>(), size, path));
        await _sut.UploadAsync(stream, path, size, _ct);
        Assert.Pass();
    }

    [Test]
    public void UploadAsync_WhenMinioThrows_Propagates()
    {
        var stream = new MemoryStream([5]);
        const string path = "docs/error.pdf";
        var ex = new InvalidOperationException("upload failed");
        _minio.Setup(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), _ct)).ThrowsAsync(ex);
        var thrown =
            Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UploadAsync(stream, path, stream.Length, _ct));
        Assert.That(thrown, Is.Not.Null);
    }
}