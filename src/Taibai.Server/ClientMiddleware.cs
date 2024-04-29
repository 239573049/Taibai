using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Taibai.Core;

namespace Taibai.Server;

public class ClientMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        
        // 如果不是http2协议，我们不处理, 因为我们只支持http2
        if (context.Request.Protocol != HttpProtocol.Http2)
        {
            return;
        }

        var name = context.Request.Query["name"];

        if (string.IsNullOrEmpty(name))
        {
            context.Response.StatusCode = 400;
            Console.WriteLine("Name is required");
            return;
        }
        
        Console.WriteLine("Accepted connection from " + name);

        var http2Feature = context.Features.Get<IHttpExtendedConnectFeature>();
        context.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();

        // 得到双工流
        var stream = new SafeWriteStream(await http2Feature.AcceptAsync());

        // 通过name找到指定的server链接，然后进行转发。
        var (cancellationToken, reader) = ServerService.GetConnectionChannel(name);

        try
        {
            // 注册取消连接
            cancellationToken.Register(() =>
            {
                Console.WriteLine("断开连接");
                stream.Close();
            });

            // 得到客户端的流，然后给我们的SafeWriteStream,然后我们就可以进行转发了
            var socketStream = new SafeWriteStream(reader);

            // 在这里他们会将读取的数据写入到对方的流中，这样就实现了双工通信，这个非常简单并且性能也不错。
            await Task.WhenAll(
                stream.CopyToAsync(socketStream, context.RequestAborted),
                socketStream.CopyToAsync(stream, context.RequestAborted)
            );
        }
        catch (Exception e)
        {
            Console.WriteLine("断开连接" + e.Message);
            throw;
        }
    }
}