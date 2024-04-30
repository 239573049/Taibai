using Microsoft.AspNetCore.Http.Features;

namespace Taibai.Server;

public class TunnelMiddleware(HttpTunnelFactory httpTunnelFactory, ILogger<TunnelMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var cyarpFeature = context.Features.GetRequiredFeature<ITaibaiFeature>();
        if (cyarpFeature.IsRequest == false)
        {
            await next(context);
            return;
        }

        var target = context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget;
        if (Guid.TryParse(context.Request.Query["tunnelId"].ToString(), out var tunnelId) == false)
        {
            await next(context);
            return;
        }

        if (httpTunnelFactory.Contains(tunnelId))
        {
            var stream = await cyarpFeature.AcceptAsStreamAsync();

            var httpTunnel = new HttpTunnel(stream, tunnelId, cyarpFeature.Protocol, logger);

            if (httpTunnelFactory.SetResult(httpTunnel))
            {
                await httpTunnel.Closed;
            }
            else
            {
                httpTunnel.Dispose();
            }
        }
    }
}