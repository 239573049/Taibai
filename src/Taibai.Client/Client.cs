using System.Net;
using System.Net.Security;
using Taibai.Core;

namespace Taibai.Client;

public class Client(string server)
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


    public async Task<Stream> HttpConnectServerCoreAsync(string clientId, CancellationToken cancellationToken)
    {
        return new SafeWriteStream(await this.Http20ConnectServerAsync(clientId, cancellationToken));
    }

    /// <summary>
    /// 创建http2连接
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<Stream> Http20ConnectServerAsync(string clientId, CancellationToken cancellationToken)
    {
        var serverUri = new Uri($"{server}/client?clientId=" + clientId);
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