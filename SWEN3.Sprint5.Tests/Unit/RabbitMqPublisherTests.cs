using SWEN3.Sprint5.Publishing;

namespace SWEN3.Sprint5.Tests.Unit;

public class RabbitMqPublisherTests
{
    private readonly Mock<IChannel> _channelMock = new();
    private readonly Mock<IConnection> _connectionMock = new();
    private readonly RabbitMqPublisher _publisher;

    public RabbitMqPublisherTests()
    {
        _connectionMock.Setup(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_channelMock.Object);
        _channelMock.Setup(c => c.DisposeAsync()).Returns(new ValueTask());
        _publisher = new RabbitMqPublisher(_connectionMock.Object);
    }

    [Fact]
    public async Task PublishAsync_ShouldCreateAndDisposeChannel()
    {
        await _publisher.PublishAsync("test.key", new { Id = 1 });
        _connectionMock.Verify(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>()), Times.Once);
        _channelMock.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task PublishAsync_WhenChannelCreationFails_ShouldThrow()
    {
        var failingConnection = new Mock<IConnection>();
        failingConnection.Setup(c => c.CreateChannelAsync(null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection closed"));
        var publisher = new RabbitMqPublisher(failingConnection.Object);
        var act = async () => await publisher.PublishAsync("test.key", new { Id = 1 });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Connection closed");
    }
}