﻿using System.Runtime.CompilerServices;

namespace Taibai.Client.Monitor;

public class ServerConnection : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly Timer? keepAliveTimer;
    private readonly TimeSpan keepAliveTimeout;

    private static readonly string Ping = "PING";
    private static readonly string Pong = "PONG";
    private static readonly ReadOnlyMemory<byte> PingLine = "PING\r\n"u8.ToArray();
    private static readonly ReadOnlyMemory<byte> PongLine = "PONG\r\n"u8.ToArray();

    public ServerConnection(Stream stream, TimeSpan keepAliveInterval)
    {
        this.stream = stream;

        if (keepAliveInterval > TimeSpan.Zero)
        {
            this.keepAliveTimeout = keepAliveInterval.Add(TimeSpan.FromSeconds(10d));
            this.keepAliveTimer = new Timer(this.KeepAliveTimerTick, null, keepAliveInterval, keepAliveInterval);
        }
        else
        {
            this.keepAliveTimeout = Timeout.InfiniteTimeSpan;
        }
    }

    /// <summary>
    /// 心跳timer
    /// </summary>
    /// <param name="state"></param>
    private async void KeepAliveTimerTick(object? state)
    {
        try
        {
            Console.WriteLine("Send PING");
            await this.stream.WriteAsync(PingLine);
        }
        catch (Exception)
        {
            if(keepAliveTimer != null)
            {
                await this.keepAliveTimer.DisposeAsync();
            }
        }
    }

    public async IAsyncEnumerable<Guid> ReadTunnelIdAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var textReader = new StreamReader(this.stream, leaveOpen: true);
        while (cancellationToken.IsCancellationRequested == false)
        {
            var textTask = textReader.ReadLineAsync(cancellationToken);
            var text = this.keepAliveTimeout <= TimeSpan.Zero
                ? await textTask
                : await textTask.AsTask().WaitAsync(this.keepAliveTimeout, cancellationToken);

            if (text == null)
            {
                yield break;
            }
            else if (text == Ping)
            {
                Console.WriteLine("receive PING");
                await this.stream.WriteAsync(PongLine, cancellationToken);
            }
            else if (text == Pong)
            {
                Console.WriteLine("receive PONG");
            }
            else if (Guid.TryParse(text, out var tunnelId))
            {
                yield return tunnelId;
            }
            else
            {
                Console.WriteLine($"Unknown news：{text}");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        this.keepAliveTimer?.Dispose();
        return this.stream.DisposeAsync();
    }
}