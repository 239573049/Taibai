using Taibai.Core;

namespace Taibai.Server;

partial class ServerMiddleware(
    HttpTunnelFactory httpTunnelFactory,
    ILogger<ServerMiddleware> logger,
    ClientManager clientManager) : IMiddleware
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

        // 创建连接
        var stream = await feature.AcceptAsSafeWriteStreamAsync();

        var connection = new ClientConnection(clientId, stream, new ConnectionConfig(), logger);

        var disconnected = false;

        await using (var client = new Client(connection, httpTunnelFactory, context))
        {
            if (await clientManager.AddAsync(client, default))
            {
                await connection.WaitForCloseAsync();

                disconnected = await clientManager.RemoveAsync(client, default);
            }
        }
    }

    private static bool IsAllowProtocol(TransportProtocol protocol)
    {
        return protocol == TransportProtocol.Http11 || protocol == TransportProtocol.Http2;
    }
}