using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Taibai.Core;
using HttpMethod = System.Net.Http.HttpMethod;

namespace Taibai.Client;


public class Client
{
    private readonly ClientOption option;

    private string Protocol = "taibai";
    private readonly HttpMessageInvoker httpClient;
    private readonly Socket socket;

    public Client(ClientOption option)
    {
        this.option = option;
        this.httpClient = new HttpMessageInvoker(CreateDefaultHttpHandler(), true);

        this.socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

        // 监听本地端口
        this.socket.Bind(new IPEndPoint(IPAddress.Loopback, 60112));
        this.socket.Listen(10);
    }

    private static SocketsHttpHandler CreateDefaultHttpHandler()
    {
        return new SocketsHttpHandler
        {
            // 允许多个http2连接
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(60),
            ResponseDrainTimeout = TimeSpan.FromSeconds(60),  
            SslOptions = new SslClientAuthenticationOptions
            {
                // 由于我们没有证书，所以我们需要设置为true
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
            },
        };
    }

    public async Task TransportAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Listening on 60112");

        // 等待客户端连接
        var client = await this.socket.AcceptAsync(cancellationToken);

        Console.WriteLine("Accepted connection from " + client.RemoteEndPoint);

        try
        {
            // 将Socket转换为流
            var stream = new NetworkStream(client);

            // 创建服务器的连接,然后返回一个流, 这个是H2的流
            var serverStream = await this.CreateServerConnectionAsync(cancellationToken);

            Console.WriteLine("Connected to server");

            // 将两个流连接起来, 这样我们就可以进行双工通信了. 它们会自动进行数据的传输.
            await Task.WhenAll(
                stream.CopyToAsync(serverStream, cancellationToken),
                serverStream.CopyToAsync(stream, cancellationToken)
            );
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    /// <summary>
    /// 创建与服务器的连接
    /// </summary> 
    /// <param name="cancellationToken"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <returns></returns>
    public async Task<SafeWriteStream> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        var stream = await this.Http20ConnectServerAsync(cancellationToken);
        return new SafeWriteStream(stream);
    }

    private async Task<Stream> Http20ConnectServerAsync(CancellationToken cancellationToken)
    {
        var serverUri = new Uri(option.ServiceUri);
        // 这里我们使用Connect方法, 因为我们需要建立一个双工流
        var request = new HttpRequestMessage(HttpMethod.Connect, serverUri);

        // 由于我们设置了Connect方法, 所以我们需要设置协议，这样服务器才能识别
        request.Headers.Protocol = Protocol;
        // 设置http2版本
        request.Version = HttpVersion.Version20;
        // 强制使用http2
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);

        // 发送请求，等待服务器验证。
        var httpResponse = await this.httpClient.SendAsync(request, linkedTokenSource.Token);

        // 返回一个流
        return await httpResponse.Content.ReadAsStreamAsync(linkedTokenSource.Token);
    }
}