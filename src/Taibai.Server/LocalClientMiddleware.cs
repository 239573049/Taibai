using Taibai.Core;

namespace Taibai.Server;

public class LocalClientMiddleware(
    ILogger<LocalClientMiddleware> logger,
    HttpTunnelFactory httpTunnelFactory,
    ClientManager clientManager)
    : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var feature = new TaibaiFeature(context);
        if (IsAllowProtocol(feature.Protocol))
        {
            context.Features.Set<ITaibaiFeature>(feature);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var clientId = context.Request.Query["clientId"].ToString();

        if (string.IsNullOrEmpty(clientId))
        {
            await next(context);
            return;
        }


        if (clientManager.TryGetValue(clientId, out var connection))
        {
            var httpTunnel =
                await httpTunnelFactory.CreateHttpTunnelAsync(connection.Connection, context.RequestAborted);
            // 通知客户端创建新的连接
            var stream = await feature.AcceptAsSafeWriteStreamAsync();
            
            var target = stream.CopyToAsync(httpTunnel, context.RequestAborted);
            var source = httpTunnel.CopyToAsync(stream, context.RequestAborted);
            Task.WaitAny(target, source);
        }
    }


    private static bool IsAllowProtocol(TransportProtocol protocol)
    {
        return protocol == TransportProtocol.Http11 || protocol == TransportProtocol.Http2;
    }
}