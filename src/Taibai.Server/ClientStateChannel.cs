using System.Threading.Channels;

namespace Taibai.Server;

/// <summary>
/// 客户端状态Channel
/// </summary>
public sealed class ClientStateChannel
{
    private readonly bool hasStateStorages;
    private readonly Channel<ClientState> channel = Channel.CreateUnbounded<ClientState>();

    /// <summary>
    /// 将客户端状态写入Channel
    /// 确保持久层的性能不影响到ClientManager
    /// </summary>
    /// <param name="client"></param>
    /// <param name="connected"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public ValueTask WriteAsync(Client client, bool connected, CancellationToken cancellationToken)
    {
        if (this.hasStateStorages == false)
        {
            return ValueTask.CompletedTask;
        }

        var clientState = new ClientState
        {
            Client = client,
            IsConnected = connected
        };

        return this.channel.Writer.WriteAsync(clientState, cancellationToken);
    }

    /// <summary>
    /// 从Channel读取所有客户端状态
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public IAsyncEnumerable<ClientState> ReadAllAsync(CancellationToken cancellationToken)
    {
        return this.channel.Reader.ReadAllAsync(cancellationToken);
    }
}