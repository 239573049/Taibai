using Taibai.Client;

const string commandTemplate = @"

当前是 Taibai 客户端，输入以下命令：

- `help` 显示帮助
- `monitor` 使用监控模式，监听本地端口，将流量转发到服务端的指定地址
    - `monitor=https://localhost:7153/server?name=test`  监听本地端口，将流量转发到服务端指定的客户端名称为 test 的地址
- `client` 使用客户端模式，连接服务端的指定地址，将流量转发到本地端口
    - `client=https://localhost:7153/client?name=test`  连接服务端指定当前客户端名称为 test，将流量转发到本地端口
- `exit` 退出

输入命令：

";

while (true)
{
    Console.WriteLine(commandTemplate);

    var command = Console.ReadLine();


    if (command?.StartsWith("monitor=") == true)
    {
        var client = new MonitorClient(new ClientOption()
        {
            ServiceUri = command[8..]
        });

        await client.TransportAsync(new CancellationToken());
    }
    else if (command?.StartsWith("client=") == true)
    {
        var client = new Client(new ClientOption()
        {
            ServiceUri = command[7..]
        });

        await client.TransportAsync(new CancellationToken());
    }
    else if (command == "help")
    {
        Console.WriteLine(commandTemplate);
    }
    else if (command == "exit")
    {
        Console.WriteLine("Bye!");
        break;
    }
    else
    {
        Console.WriteLine("未知命令");
    }
}