using System.Net;
using Microsoft.AspNetCore.Http.Features;
using Taibai.Core;

namespace Taibai.Server;

public partial class Client
{
    private volatile bool disposed = false;
    public readonly ClientConnection Connection;
    private readonly HttpTunnelFactory httpTunnelFactory;
    private readonly HttpContext httpContext;
    private readonly Lazy<HttpMessageInvoker> httpClientLazy;

    public string Id => this.Connection.ClientId;



    public TransportProtocol Protocol => this.httpContext.Features.GetRequiredFeature<ITaibaiFeature>().Protocol;

    public int HttpTunnelCount => this.Connection.HttpTunnelCount;

    public IPEndPoint? RemoteEndpoint
    {
        get
        {
            var connection = this.httpContext.Connection;
            return connection.RemoteIpAddress == null
                ? null
                : new IPEndPoint(connection.RemoteIpAddress, connection.RemotePort);
        }
    }

    public DateTimeOffset CreationTime { get; } = DateTimeOffset.Now;


    public Client(
        ClientConnection connection,
        HttpTunnelFactory httpTunnelFactory,
        HttpContext httpContext)
    {
        this.Connection = connection;
        this.httpTunnelFactory = httpTunnelFactory;
        this.httpContext = httpContext;
    }

    public async ValueTask DisposeAsync()
    {
        if (this.disposed == false)
        {
            this.disposed = true;

            if (this.httpClientLazy.IsValueCreated)
            {
                this.httpClientLazy.Value.Dispose();
            }

            await this.Connection.DisposeAsync();
        }
    }

    public override string ToString()
    {
        return this.Id;
    }
}