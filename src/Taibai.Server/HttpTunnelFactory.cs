﻿using System.Collections.Concurrent;
using System.Diagnostics;
using Taibai.Core;

namespace Taibai.Server;

public sealed partial class HttpTunnelFactory(ILogger<HttpTunnel> logger)
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<HttpTunnel>> httpTunnelCompletionSources =
        new();

    /// <summary>
    /// 创建HttpTunnel
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<HttpTunnel> CreateHttpTunnelAsync(ClientConnection connection,
        CancellationToken cancellationToken)
    {
        var tunnelId = Guid.NewGuid();
        var httpTunnelSource = new TaskCompletionSource<HttpTunnel>();
        if (this.httpTunnelCompletionSources.TryAdd(tunnelId, httpTunnelSource) == false)
        {
            throw new SystemException($"系统中已存在{tunnelId}的tunnelId");
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            Log.LogTunnelCreating(logger, connection.ClientId, tunnelId);
            await connection.CreateHttpTunnelAsync(tunnelId, cancellationToken);
            var httpTunnel = await httpTunnelSource.Task.WaitAsync(cancellationToken);

            var httpTunnelCount = connection.IncrementHttpTunnelCount();
            httpTunnel.BindConnection(connection);

            stopwatch.Stop();
            Log.LogTunnelCreateSuccess(logger, connection.ClientId, httpTunnel.Protocol, tunnelId,
                stopwatch.Elapsed, httpTunnelCount);
            return httpTunnel;
        }
        catch (OperationCanceledException)
        {
            Log.LogTunnelCreateFailure(logger, connection.ClientId, tunnelId, "远程端操作超时");
            throw;
        }
        catch (Exception ex)
        {
            Log.LogTunnelCreateFailure(logger, connection.ClientId, tunnelId, ex.Message);
            throw;
        }
        finally
        {
            this.httpTunnelCompletionSources.TryRemove(tunnelId, out _);
        }
    }

    public bool Contains(Guid tunnelId)
    {
        return this.httpTunnelCompletionSources.ContainsKey(tunnelId);
    }

    public bool SetResult(HttpTunnel httpTunnel)
    {
        return this.httpTunnelCompletionSources.TryRemove(httpTunnel.Id, out var source) &&
               source.TrySetResult(httpTunnel);
    }

    static partial class Log
    {
        [LoggerMessage(LogLevel.Information, "[{clientId}] 请求创建隧道{tunnelId}")]
        public static partial void LogTunnelCreating(ILogger logger, string clientId, Guid tunnelId);

        [LoggerMessage(LogLevel.Warning, "[{clientId}] 创建隧道{tunnelId}失败：{reason}")]
        public static partial void LogTunnelCreateFailure(ILogger logger, string clientId, Guid tunnelId,
            string? reason);

        [LoggerMessage(LogLevel.Information,
            "[{clientId}] 创建了{protocol}协议隧道{tunnelId}，过程耗时{elapsed}，其当前隧道总数为{tunnelCount}")]
        public static partial void LogTunnelCreateSuccess(ILogger logger, string clientId, TransportProtocol protocol,
            Guid tunnelId, TimeSpan elapsed, int tunnelCount);
    }
}