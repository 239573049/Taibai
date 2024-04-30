using System.Diagnostics;

namespace Taibai.Client.Monitor;

public class ServerClient(MonitorServer monitorServer, string server, string clientId, string host, int port)
    : IDisposable
{
    private int tunnelCount;

    public async Task TransportCoreAsync(CancellationToken cancellationToken)
    {
        await using var connection =
            await monitorServer.CreateServerConnectionAsync(server,
                clientId,
                cancellationToken);

        using var connectionTokenSource = new CancellationTokenSource();
        try
        {
            using var linkedTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionTokenSource.Token);
            await foreach (var tunnelId in connection.ReadTunnelIdAsync(cancellationToken))
            {
                this.BindTunnelIOAsync(tunnelId, linkedTokenSource.Token);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            await connectionTokenSource.CancelAsync();
        }
    }


    /// <summary>
    /// 绑定tunnel的IO
    /// </summary> 
    /// <param name="tunnelId"></param>
    /// <param name="cancellationToken"></param>
    private async void BindTunnelIOAsync(Guid tunnelId, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var targetTunnel = await monitorServer.CreateTargetTunnelAsync(host, port, cancellationToken);

            await using var serverTunnel =
                await monitorServer.CreateServerTunnelAsync(server, tunnelId, cancellationToken);

            var count = Interlocked.Increment(ref this.tunnelCount);
            // Log.LogTunnelCreated(this.logger, tunnelId, stopwatch.Elapsed, count);

            var server2Target = serverTunnel.CopyToAsync(targetTunnel, cancellationToken);
            var target2Server = targetTunnel.CopyToAsync(serverTunnel, cancellationToken);
            var task = await Task.WhenAny(server2Target, target2Server);

            count = Interlocked.Decrement(ref this.tunnelCount);

            // if (task == server2Target)
            // {
            //     Log.LogTunnelClosed(this.logger, tunnelId, this.options.ServerUri, stopwatch.Elapsed, count);
            // }
            // else
            // {
            //     Log.LogTunnelClosed(this.logger, tunnelId, this.options.TargetUri, stopwatch.Elapsed, count);
            // }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // this.OnTunnelException(ex);
            // Log.LogTunnelError(this.logger, tunnelId, ex.Message);
        }
        finally
        {
            stopwatch.Stop();
        }
        
    }

    public void Dispose()
    {
    }
}