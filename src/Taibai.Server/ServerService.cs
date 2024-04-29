using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Taibai.Core;

namespace Taibai.Server;

public static class ServerService
{
    private static readonly ConcurrentDictionary<string, (CancellationToken, Stream)> ClusterConnections = new();

    public static async Task StartAsync(HttpContext context)
    {
        // 如果不是http2协议，我们不处理, 因为我们只支持http2
        if (context.Request.Protocol != HttpProtocol.Http2)
        {
            return;
        }

        // 获取query
        var query = context.Request.Query;

        // 我们需要强制要求name参数
        var name = query["name"];

        if (string.IsNullOrEmpty(name))
        {
            context.Response.StatusCode = 400;
            Console.WriteLine("Name is required");
            return;
        }
        
        Console.WriteLine("Accepted connection from " + name);

        // 获取http2特性
        var http2Feature = context.Features.Get<IHttpExtendedConnectFeature>();
        
        // 禁用超时
        context.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();

        // 得到双工流
        var stream = new SafeWriteStream(await http2Feature.AcceptAsync());

        // 将其添加到集合中，以便我们可以在其他地方使用
        CreateConnectionChannel(name, context.RequestAborted, stream);

        // 注册取消连接
        context.RequestAborted.Register(() =>
        {
            // 当取消时，我们需要从集合中删除
            ClusterConnections.TryRemove(name, out _);
        });
        
        // 由于我们需要保持连接，所以我们需要等待，直到客户端主动断开连接。
        await Task.Delay(-1, context.RequestAborted);
    }

    /// <summary>
    /// 通过名称获取连接
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static (CancellationToken, Stream) GetConnectionChannel(string host)
    {
        return ClusterConnections[host];
    }

    /// <summary>
    /// 注册连接
    /// </summary>
    /// <param name="host"></param>
    /// <param name="cancellationToken"></param>
    /// <param name="stream"></param>
    public static void CreateConnectionChannel(string host, CancellationToken cancellationToken, Stream stream)
    {
        ClusterConnections.GetOrAdd(host,
            _ => (cancellationToken, stream));
    }
}