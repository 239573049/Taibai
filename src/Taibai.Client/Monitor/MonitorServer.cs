using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Taibai.Core;

namespace Taibai.Client.Monitor;

public class MonitorServer
{

    private readonly HttpMessageInvoker httpClient = new(CreateDefaultHttpHandler(), true);

    private static SocketsHttpHandler CreateDefaultHttpHandler()
    {
        return new SocketsHttpHandler
        {
            // 允许多个http2连接
            EnableMultipleHttp2Connections = true,
            // 设置连接超时时间
            ConnectTimeout = TimeSpan.FromSeconds(60),
            SslOptions = new SslClientAuthenticationOptions
            {
                // 由于我们没有证书，所以我们需要设置为true
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };
    }


    private async Task<Stream> HttpConnectServerCoreAsync(string server, string clientId, Guid? tunnelId,
        CancellationToken cancellationToken)
    {
        return await this.Http20ConnectServerAsync(server, clientId, tunnelId, cancellationToken);
    }

    public async Task<ServerConnection> CreateServerConnectionAsync(string server, string clientId,
        CancellationToken cancellationToken)
    {
        var stream = await this.HttpConnectServerCoreAsync(server, clientId, null, cancellationToken);
        var safeWriteStream = new SafeWriteStream(stream);
        return new ServerConnection(safeWriteStream, TimeSpan.FromSeconds(60));
    }

    public async Task<Stream> ConnectServerAsync(string server, Guid? tunnelId, CancellationToken cancellationToken)
    {
        return await this.HttpConnectServerCoreAsync(server, null, tunnelId, cancellationToken);
    }

    /// <summary>
    /// 创建到服务器的通道
    /// </summary>
    /// <param name="server"></param>
    /// <param name="tunnelId"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <returns></returns>
    public async Task<Stream> CreateServerTunnelAsync(string server, Guid tunnelId,
        CancellationToken cancellationToken)
    {
        var stream = await this.ConnectServerAsync(server, tunnelId, cancellationToken);
        return new ForceFlushStream(stream);
    }


    /// <summary>
    /// 创建到目的地的通道
    /// </summary>
    /// <param name="host"></param>
    /// <param name="port"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <returns></returns>
    public async Task<Stream> CreateTargetTunnelAsync(string host, int port, CancellationToken cancellationToken)
    {
        EndPoint endPoint = new DnsEndPoint(host, port);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            using var timeoutTokenSource = new CancellationTokenSource(60);
            using var linkedTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);
            await socket.ConnectAsync(endPoint, linkedTokenSource.Token);
            return new NetworkStream(socket);
        }
        catch (Exception ex)
        {
            socket.Dispose();
            throw;
        }
    }


    /// <summary>
    /// 创建http2连接
    /// </summary>
    /// <param name="server"></param>
    /// <param name="clientId"></param>
    /// <param name="tunnelId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<Stream> Http20ConnectServerAsync(string server, string? clientId, Guid? tunnelId,
        CancellationToken cancellationToken)
    {
        Uri serverUri;
        if (tunnelId == null)
        {
            serverUri = new Uri($"{server.TrimEnd('/')}/server?clientId=" + clientId);
        }
        else
        {
            serverUri = new Uri($"{server.TrimEnd('/')}/server?tunnelId=" + tunnelId);
        }

        // 这里我们使用Connect方法，因为我们需要建立一个双工流, 这样我们就可以进行双工通信了。
        var request = new HttpRequestMessage(HttpMethod.Connect, serverUri);
        // 如果设置了Connect，那么我们需要设置Protocol
        request.Headers.Protocol = Constant.Protocol;
        // 我们需要设置http2的版本
        request.Version = HttpVersion.Version20;

        // 我们需要确保我们的请求是http2的
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        // 设置一下超时时间，这样我们就可以在超时的时候取消连接了。
        using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);

        // 发送请求，然后等待响应
        var httpResponse = await this.httpClient.SendAsync(request, linkedTokenSource.Token);

        // 返回h2的流，用于传输数据
        return await httpResponse.Content.ReadAsStreamAsync(linkedTokenSource.Token);
    }
}