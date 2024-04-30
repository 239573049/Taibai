using Taibai.Core;

namespace Taibai.Server;

/// <summary>
/// http隧道
/// </summary>
public sealed partial class HttpTunnel(Stream inner, Guid tunnelId, TransportProtocol protocol, ILogger logger)
    : DelegatingStream(inner)
{
    private ClientConnection? connection;
    private readonly long tickCout = Environment.TickCount64;
    private readonly TaskCompletionSource closeTaskCompletionSource = new();

    /// <summary>
    /// 等待HttpClient对其关闭
    /// </summary>
    public Task Closed => this.closeTaskCompletionSource.Task;

    /// <summary>
    /// 隧道标识
    /// </summary>
    public Guid Id { get; } = tunnelId;

    /// <summary>
    /// 传输协议
    /// </summary>
    public TransportProtocol Protocol { get; } = protocol;

    public void BindConnection(ClientConnection connection)
    {
        this.connection = connection;
    }

    public override ValueTask DisposeAsync()
    {
        this.SetClosedResult();
        return this.Inner.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        this.SetClosedResult();
        this.Inner.Dispose();
    }

    private void SetClosedResult()
    {
        if (this.closeTaskCompletionSource.TrySetResult())
        {
            var httpTunnelCount = this.connection?.DecrementHttpTunnelCount();
            var lifeTime = TimeSpan.FromMilliseconds(Environment.TickCount64 - this.tickCout);
            Log.LogTunnelClosed(logger, this.connection?.ClientId, this.Protocol, this.Id, lifeTime,
                httpTunnelCount);
        }
    }

    public override string ToString()
    {
        return this.Id.ToString();
    }

    static partial class Log
    {
        [LoggerMessage(LogLevel.Information,
            "[{clientId}] 关闭了{protocol}协议隧道{tunnelId}，生命周期为{lifeTime}，其当前隧道总数为{tunnelCount}")]
        public static partial void LogTunnelClosed(ILogger logger, string? clientId, TransportProtocol protocol,
            Guid tunnelId, TimeSpan lifeTime, int? tunnelCount);
    }
}