using System.Net;
using System.Net.Sockets;
using Taibai.Client;
using Taibai.Client.Monitor;

const string commandTemplate = @"

当前是 Taibai 客户端，输入以下命令：

- `help` 显示帮助
    - `monitor` 提供默认使用案例
    - `client` 提供默认使用案例
- `monitor` 使用监控模式，监听本地端口，将本地的指定的服务映射到server，只需要指定clientId
    - `clientId` 指定当前客户端的ID
    - `server` 指定服务端地址
    - `host` 代理的目标地址
    - `port` 代理的目标端口
- `client` 使用客户端模式，将本地的指定的服务映射到server，需要指定clientId，然后监听指定的host和port，然后访问本地的host和port就可以转发到monitor的host和port。
    - `server` 指定服务端地址
    - `clientId` 指定当前客户端的ID
    - `host` 代理的目标地址
    - `port` 代理的目标端口
- `exit` 退出

输入命令：

";

var token = new CancellationTokenSource();

while (true)
{
    Console.WriteLine(commandTemplate);

    var command = Console.ReadLine();

    if (command == "exit")
    {
        break;
    }

    if (command?.StartsWith("help") == true)
    {
        if (command == "help")
        {
            Console.WriteLine(commandTemplate);

            continue;
        }

        if (command == "help monitor")
        {
            Console.WriteLine(@"monitor clientId=test server=https://127.0.0.1:5001 host=192.168.31.250 port=3389");
        }
        else if (command == "help client")
        {
            Console.WriteLine(@"client server=https://127.0.0.1:5001 host=127.0.0.1 port=60001 clientId=test");
        }
        else
        {
            Console.WriteLine("Unknown help command");
        }

        continue;
    }

    // monitor clientId=xxx server=xxx host=xxx port=xxx 但是他们的位置不固定
    if (command?.StartsWith("monitor") == true)
    {
        Console.WriteLine("monitor mode");

        var parameters = new ParseHelper(command);

        parameters.TryGetRequiredValue("clientId", out string clientId);
        parameters.TryGetRequiredValue("server", out string server);
        parameters.TryGetRequiredValue("host", out string host);
        parameters.TryGetRequiredValue("port", out int port);

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentException("port must be between 0 and 65535");
        }

        var monitorServer = new MonitorServer();

        var serverClient = new ServerClient(monitorServer, server, clientId, host, port);

        await serverClient.TransportCoreAsync(CancellationToken.None);
    }
    else if (command?.StartsWith("client") == true)
    {
        Console.WriteLine("client mode");

        var parameters = new ParseHelper(command);

        parameters.TryGetRequiredValue("server", out string server);
        parameters.TryGetRequiredValue("host", out string host);
        parameters.TryGetRequiredValue("port", out int port);
        parameters.TryGetRequiredValue("clientId", out string clientId);

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentException("port must be between 0 and 65535");
        }

        var client = new Client(server);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

        socket.Bind(new IPEndPoint(IPAddress.Parse(host), port));

        socket.Listen(10);

        while (token.IsCancellationRequested == false)
        {
            await Task.Factory.StartNew(HandlerAsync, await socket.AcceptAsync());
            Console.WriteLine("有客户端连接进来了");
        }

        async Task HandlerAsync(object state)
        {
            if (state is Socket socket)
            {
                var stream = await client.HttpConnectServerCoreAsync(clientId, token.Token);

                Console.WriteLine("连接成功");

                await Task.WhenAny(
                    stream.CopyToAsync(new NetworkStream(socket), token.Token),
                    new NetworkStream(socket).CopyToAsync(stream, token.Token)
                );
            }
        }
    }
    else
    {
        Console.WriteLine("Unknown command");
    }
}

class ParseHelper(string command)
{
    private Dictionary<string, string> _parameters = ParseParameters(command);

    static Dictionary<string, string> ParseParameters(string input)
    {
        var parameters = new Dictionary<string, string>();
        var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var keyValue = part.Split(new[] { '=' }, 2);
            if (keyValue.Length == 2)
            {
                parameters[keyValue[0]] = keyValue[1];
            }
        }

        return parameters;
    }

    public string this[string key] => _parameters[key];

    public bool TryGetValue(string key, out string? value) => _parameters.TryGetValue(key, out value);

    public bool ContainsKey(string key) => _parameters.ContainsKey(key);

    public bool TryGetRequiredValue(string key, out string value)
    {
        if (_parameters.TryGetValue(key, out value))
        {
            return true;
        }

        throw new ArgumentException($"Missing required parameter: {key}");
    }

    public bool TryGetRequiredValue(string key, out int value)
    {
        if (_parameters.TryGetValue(key, out var stringValue) && int.TryParse(stringValue, out value))
        {
            return true;
        }

        throw new ArgumentException($"Missing required parameter: {key}");
    }
}