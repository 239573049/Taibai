using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Taibai.Core;

namespace Taibai.Client;

public class MonitorClient(ClientOption option)
{
    private string Protocol = "taibai";
    private readonly HttpMessageInvoker httpClient = new(CreateDefaultHttpHandler(), true);
    private readonly Socket socket = new(SocketType.Stream, ProtocolType.Tcp);

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

    public async Task TransportAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("链接中！");

        // 由于是测试，我们就目前先写死远程地址
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("192.168.31.250"), 3389), cancellationToken);

        Console.WriteLine("连接成功");

        // 将Socket转换为流
        var stream = new NetworkStream(socket);
        try
        {
            // 创建服务器的连接，然后返回一个流，这个是H2的流
            var serverStream = await this.CreateServerConnectionAsync(cancellationToken);

            Console.WriteLine("链接服务器成功");

            // 将两个流连接起来，这样我们就可以进行双工通信了。它们会自动进行数据的传输。
            await Task.WhenAll(
                stream.CopyToAsync(serverStream, cancellationToken),
                serverStream.CopyToAsync(stream, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 创建服务器的连接
    /// </summary> 
    /// <param name="cancellationToken"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <returns></returns>
    public async Task<SafeWriteStream> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        var stream = await Http20ConnectServerAsync(cancellationToken);
        return new SafeWriteStream(stream);
    }

    /// <summary>
    /// 创建http2连接
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<Stream> Http20ConnectServerAsync(CancellationToken cancellationToken)
    {
        var serverUri = new Uri(option.ServiceUri);
        // 这里我们使用Connect方法，因为我们需要建立一个双工流, 这样我们就可以进行双工通信了。
        var request = new HttpRequestMessage(HttpMethod.Connect, serverUri);
        // 如果设置了Connect，那么我们需要设置Protocol
        request.Headers.Protocol = Protocol;
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