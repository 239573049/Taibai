# 基于H2协议转发TCP服务

可能有人很疑惑`应用层 转发传输层？`，为什么会有这样的需求啊？？？哈哈技术无所不用其极，由于一些场景下，对于一个服务器存在某一个内部网站中，但是对于这个服务器它没有访问外网的权限，虽然也可以申请端口访问外部指定的ip+端口，但是对于访问服务内部的TCP的时候我们就会发现忘记申请了！这个时候我们又要提交申请，又要等审批，然后开通端口，对于这个步骤不是一般的麻烦，所以我在想是否可以直接利用现有的Http网关的端口进行转发内部的TCP服务？这个时候我询问了我们的`老九`大佬，由于我之前也做过通过H2实现HTTP内网穿透，可以利用H2将内部网络中的服务映射出来，但是由于底层是基于yarp的一些方法实现，所以并没有考虑过TCP，然后于`老九`大佬交流深究，决定尝试验证可行性，然后我们的`Taibai`项目就诞生了，为什么叫`Taibai`？您仔细看看这个拼音，翻译过来就是太白，确实全称应该叫太白金星，寓意上天遁地无所不能！下面我们介绍一下具体实现逻辑，确实您仔细看会发现实现是真的超级简单的！

## 创建Core项目用于共用的核心类库

创建项目名`Taibai.Core`

下面几个方法都是用于操作Stream的类

`DelegatingStream.cs`

```csharp
namespace Taibai.Core;

/// <summary>
/// 委托流
/// </summary>
public abstract class DelegatingStream : Stream
{
    /// <summary>
    /// 获取所包装的流对象
    /// </summary>
    protected readonly Stream Inner;

    /// <summary>
    /// 委托流
    /// </summary>
    /// <param name="inner"></param>
    public DelegatingStream(Stream inner)
    {
        this.Inner = inner;
    }

    /// <inheritdoc/>
    public override bool CanRead => Inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => Inner.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => Inner.CanWrite;

    /// <inheritdoc/>
    public override long Length => Inner.Length;

    /// <inheritdoc/>
    public override bool CanTimeout => Inner.CanTimeout;

    /// <inheritdoc/>
    public override int ReadTimeout
    {
        get => Inner.ReadTimeout;
        set => Inner.ReadTimeout = value;
    }

    /// <inheritdoc/>
    public override int WriteTimeout
    {
        get => Inner.WriteTimeout;
        set => Inner.WriteTimeout = value;
    }


    /// <inheritdoc/>
    public override long Position
    {
        get => Inner.Position;
        set => Inner.Position = value;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        Inner.Flush();
    }

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Inner.FlushAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return Inner.Read(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override int Read(Span<byte> destination)
    {
        return Inner.Read(destination);
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return Inner.ReadAsync(buffer, offset, count, cancellationToken);
    }

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        return Inner.ReadAsync(destination, cancellationToken);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return Inner.Seek(offset, origin);
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        Inner.SetLength(value);
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        Inner.Write(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> source)
    {
        Inner.Write(source);
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return Inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        return Inner.WriteAsync(source, cancellationToken);
    }

    /// <inheritdoc/>
    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state)
    {
        return TaskToAsyncResult.Begin(ReadAsync(buffer, offset, count), callback, state);
    }

    /// <inheritdoc/>
    public override int EndRead(IAsyncResult asyncResult)
    {
        return TaskToAsyncResult.End<int>(asyncResult);
    }

    /// <inheritdoc/>
    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback,
        object? state)
    {
        return TaskToAsyncResult.Begin(WriteAsync(buffer, offset, count), callback, state);
    }

    /// <inheritdoc/>
    public override void EndWrite(IAsyncResult asyncResult)
    {
        TaskToAsyncResult.End(asyncResult);
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        return Inner.ReadByte();
    }

    /// <inheritdoc/>
    public override void WriteByte(byte value)
    {
        Inner.WriteByte(value);
    }

    /// <inheritdoc/>
    public sealed override void Close()
    {
        base.Close();
    }
}
```

`SafeWriteStream.cs`

```csharp
public class SafeWriteStream(Stream inner) : DelegatingStream(inner)
{
    private readonly SemaphoreSlim semaphoreSlim = new(1, 1);

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
    {
        try
        {
            await this.semaphoreSlim.WaitAsync(CancellationToken.None);
            await base.WriteAsync(source, cancellationToken);
            await this.FlushAsync(cancellationToken);
        }
        finally
        {
            this.semaphoreSlim.Release();
        }
    }

    public override ValueTask DisposeAsync()
    {
        this.semaphoreSlim.Dispose();
        return this.Inner.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        this.semaphoreSlim.Dispose();
        this.Inner.Dispose();
    }
}
```

## 创建服务端

创建一个`WebAPI`的项目项目名`Taibai.Server`并且依赖`Taibai.Core`项目

创建`ServerService.cs`，这个类是用于管理内网的客户端的，这个一般是部署在内网服务器上，用于将内网的端口映射出来，但是我们的Demo只实现了简单的管理不做端口的管理。

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Taibai.Core;

namespace Taibai.Server;

public static class ServerService
{
    private static readonly ConcurrentDictionary<string, (CancellationToken, Stream)> ClusterConnections = new();

    public static async Task StartAsync(HttpContext context)
    {
        // 如果不是http2协议，我们不处理, 因为我们只支持http2
        if (context.Request.Protocol != HttpProtocol.Http2)
        {
            return;
        }

        // 获取query
        var query = context.Request.Query;

        // 我们需要强制要求name参数
        var name = query["name"];

        if (string.IsNullOrEmpty(name))
        {
            context.Response.StatusCode = 400;
            Console.WriteLine("Name is required");
            return;
        }
        
        Console.WriteLine("Accepted connection from " + name);

        // 获取http2特性
        var http2Feature = context.Features.Get<IHttpExtendedConnectFeature>();
        
        // 禁用超时
        context.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();

        // 得到双工流
        var stream = new SafeWriteStream(await http2Feature.AcceptAsync());

        // 将其添加到集合中，以便我们可以在其他地方使用
        CreateConnectionChannel(name, context.RequestAborted, stream);

        // 注册取消连接
        context.RequestAborted.Register(() =>
        {
            // 当取消时，我们需要从集合中删除
            ClusterConnections.TryRemove(name, out _);
        });
        
        // 由于我们需要保持连接，所以我们需要等待，直到客户端主动断开连接。
        await Task.Delay(-1, context.RequestAborted);
    }

    /// <summary>
    /// 通过名称获取连接
    /// </summary>
    /// <param name="host"></param>
    /// <returns></returns>
    public static (CancellationToken, Stream) GetConnectionChannel(string host)
    {
        return ClusterConnections[host];
    }

    /// <summary>
    /// 注册连接
    /// </summary>
    /// <param name="host"></param>
    /// <param name="cancellationToken"></param>
    /// <param name="stream"></param>
    public static void CreateConnectionChannel(string host, CancellationToken cancellationToken, Stream stream)
    {
        ClusterConnections.GetOrAdd(host,
            _ => (cancellationToken, stream));
    }
}
```

然后再创建`ClientMiddleware.cs`，并且继承`IMiddleware`，这个是我们本地使用的客户端链接的时候进入的中间件，再这个中间件会获取query中携带的name去找到指定的Stream，然后会将客户端的Stream和获取的server的Stream进行Copy，在这里他们会将读取的数据写入到对方的流中，这样就实现了双工通信

```csharp
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Taibai.Core;

namespace Taibai.Server;

public class ClientMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        
        // 如果不是http2协议，我们不处理, 因为我们只支持http2
        if (context.Request.Protocol != HttpProtocol.Http2)
        {
            return;
        }

        var name = context.Request.Query["name"];

        if (string.IsNullOrEmpty(name))
        {
            context.Response.StatusCode = 400;
            Console.WriteLine("Name is required");
            return;
        }
        
        Console.WriteLine("Accepted connection from " + name);

        var http2Feature = context.Features.Get<IHttpExtendedConnectFeature>();
        context.Features.Get<IHttpRequestTimeoutFeature>()?.DisableTimeout();

        // 得到双工流
        var stream = new SafeWriteStream(await http2Feature.AcceptAsync());

        // 通过name找到指定的server链接，然后进行转发。
        var (cancellationToken, reader) = ServerService.GetConnectionChannel(name);

        try
        {
            // 注册取消连接
            cancellationToken.Register(() =>
            {
                Console.WriteLine("断开连接");
                stream.Close();
            });

            // 得到客户端的流，然后给我们的SafeWriteStream,然后我们就可以进行转发了
            var socketStream = new SafeWriteStream(reader);

            // 在这里他们会将读取的数据写入到对方的流中，这样就实现了双工通信，这个非常简单并且性能也不错。
            await Task.WhenAll(
                stream.CopyToAsync(socketStream, context.RequestAborted),
                socketStream.CopyToAsync(stream, context.RequestAborted)
            );
        }
        catch (Exception e)
        {
            Console.WriteLine("断开连接" + e.Message);
            throw;
        }
    }
}
```

打开`Program.cs`

```csharp
using Taibai.Server;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions());

builder.Host.ConfigureHostOptions(host => { host.ShutdownTimeout = TimeSpan.FromSeconds(1d); });

builder.Services.AddSingleton<ClientMiddleware>();

var app = builder.Build();

app.Map("/server", app =>
{
    app.Use(Middleware);

    static async Task Middleware(HttpContext context, RequestDelegate _)
    {
        await ServerService.StartAsync(context);
    }
});

app.Map("/client", app => { app.UseMiddleware<ClientMiddleware>(); });

app.Run();
```



在这里我们将server的所有路由都交过`ServerService.StartAsync`接管，再`server`会请求这个地址，

而`/client`则给了`ClientMiddleware`中间件。

## 创建客户端

上面我们实现了服务端，其实服务端可以完全放置到现有的WebApi项目当中的，而且代码也不是很多。

客户端我们创建一个控制台项目名：`Taibai.Client`，并且依赖`Taibai.Core`项目

由于我们的客户端有些特殊，再server中部署的它不需要监听端口，它只需要将服务器的数据转发到指定的一个地址即可，所以我们需要将客户端的server部署的和本地部署的分开实现，再服务器部署的客户端我们命名为`MonitorClient.cs`

`ClientOption.cs`用于传递我们的客户端地址配置

```csharp
public class ClientOption
{
    /// <summary>
    /// 服务地址
    /// </summary>
    public string ServiceUri { get; set; }
    
}
```

`MonitorClient.cs`，作为服务器的转发客户端。

```csharp
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Taibai.Core;

namespace Taibai.Client;

public class MonitorClient(ClientOption option)
{
    private string Protocol = "taibai";
    private readonly HttpMessageInvoker httpClient = new(CreateDefaultHttpHandler(), true);
    private readonly Socket socket = new(SocketType.Stream, ProtocolType.Tcp);

    private static SocketsHttpHandler CreateDefaultHttpHandler()
    {
        return new SocketsHttpHandler
        {
            // 允许多个http2连接
            EnableMultipleHttp2Connections = true,
            // 设置连接超时时间
            ConnectTimeout = TimeSpan.FromSeconds(60),
            SslOptions = new SslClientAuthenticationOptions
            {
                // 由于我们没有证书，所以我们需要设置为true
                RemoteCertificateValidationCallback = (_, _, _, _) => true,
            },
        };
    }

    public async Task TransportAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("链接中！");

        // 由于是测试，我们就目前先写死远程地址
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("192.168.31.250"), 3389), cancellationToken);

        Console.WriteLine("连接成功");

        // 将Socket转换为流
        var stream = new NetworkStream(socket);
        try
        {
            // 创建服务器的连接，然后返回一个流，这个是H2的流
            var serverStream = await this.CreateServerConnectionAsync(cancellationToken);

            Console.WriteLine("链接服务器成功");

            // 将两个流连接起来，这样我们就可以进行双工通信了。它们会自动进行数据的传输。
            await Task.WhenAll(
                stream.CopyToAsync(serverStream, cancellationToken),
                serverStream.CopyToAsync(stream, cancellationToken)
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    /// <summary>
    /// 创建服务器的连接
    /// </summary> 
    /// <param name="cancellationToken"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <returns></returns>
    public async Task<SafeWriteStream> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        var stream = await Http20ConnectServerAsync(cancellationToken);
        return new SafeWriteStream(stream);
    }

    /// <summary>
    /// 创建http2连接
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<Stream> Http20ConnectServerAsync(CancellationToken cancellationToken)
    {
        var serverUri = new Uri(option.ServiceUri);
        // 这里我们使用Connect方法，因为我们需要建立一个双工流, 这样我们就可以进行双工通信了。
        var request = new HttpRequestMessage(HttpMethod.Connect, serverUri);
        // 如果设置了Connect，那么我们需要设置Protocol
        request.Headers.Protocol = Protocol;
        // 我们需要设置http2的版本
        request.Version = HttpVersion.Version20;
        
        // 我们需要确保我们的请求是http2的
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        // 设置一下超时时间，这样我们就可以在超时的时候取消连接了。
        using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);

        // 发送请求，然后等待响应
        var httpResponse = await this.httpClient.SendAsync(request, linkedTokenSource.Token);

        // 返回h2的流，用于传输数据
        return await httpResponse.Content.ReadAsStreamAsync(linkedTokenSource.Token);
    }
}
```

创建我们的本地客户端实现类。

`Client.cs`这个就是在我们本地部署的服务，然后会监听本地的60112的端口，然后会吧这个端口的数据转发到我们的服务器，然后服务器会根据我们使用的name去找到指定的客户端进行交互传输。

```csharp
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Taibai.Core;
using HttpMethod = System.Net.Http.HttpMethod;

namespace Taibai.Client;


public class Client
{
    private readonly ClientOption option;

    private string Protocol = "taibai";
    private readonly HttpMessageInvoker httpClient;
    private readonly Socket socket;

    public Client(ClientOption option)
    {
        this.option = option;
        this.httpClient = new HttpMessageInvoker(CreateDefaultHttpHandler(), true);

        this.socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

        // 监听本地端口
        this.socket.Bind(new IPEndPoint(IPAddress.Loopback, 60112));
        this.socket.Listen(10);
    }

    private static SocketsHttpHandler CreateDefaultHttpHandler()
    {
        return new SocketsHttpHandler
        {
            // 允许多个http2连接
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(60),
            ResponseDrainTimeout = TimeSpan.FromSeconds(60),  
            SslOptions = new SslClientAuthenticationOptions
            {
                // 由于我们没有证书，所以我们需要设置为true
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true,
            },
        };
    }

    public async Task TransportAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Listening on 60112");

        // 等待客户端连接
        var client = await this.socket.AcceptAsync(cancellationToken);

        Console.WriteLine("Accepted connection from " + client.RemoteEndPoint);

        try
        {
            // 将Socket转换为流
            var stream = new NetworkStream(client);

            // 创建服务器的连接,然后返回一个流, 这个是H2的流
            var serverStream = await this.CreateServerConnectionAsync(cancellationToken);

            Console.WriteLine("Connected to server");

            // 将两个流连接起来, 这样我们就可以进行双工通信了. 它们会自动进行数据的传输.
            await Task.WhenAll(
                stream.CopyToAsync(serverStream, cancellationToken),
                serverStream.CopyToAsync(stream, cancellationToken)
            );
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    /// <summary>
    /// 创建与服务器的连接
    /// </summary> 
    /// <param name="cancellationToken"></param>
    /// <exception cref="OperationCanceledException"></exception>
    /// <returns></returns>
    public async Task<SafeWriteStream> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        var stream = await this.Http20ConnectServerAsync(cancellationToken);
        return new SafeWriteStream(stream);
    }

    private async Task<Stream> Http20ConnectServerAsync(CancellationToken cancellationToken)
    {
        var serverUri = new Uri(option.ServiceUri);
        // 这里我们使用Connect方法, 因为我们需要建立一个双工流
        var request = new HttpRequestMessage(HttpMethod.Connect, serverUri);

        // 由于我们设置了Connect方法, 所以我们需要设置协议，这样服务器才能识别
        request.Headers.Protocol = Protocol;
        // 设置http2版本
        request.Version = HttpVersion.Version20;
        // 强制使用http2
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        using var timeoutTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(timeoutTokenSource.Token, cancellationToken);

        // 发送请求，等待服务器验证。
        var httpResponse = await this.httpClient.SendAsync(request, linkedTokenSource.Token);

        // 返回一个流
        return await httpResponse.Content.ReadAsStreamAsync(linkedTokenSource.Token);
    }
}
```

然后再`Program.cs`中，我们封装一个简单的控制台版本。

```csharp
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
```

我们默认提供了命令去使用指定的一个模式去链接客户端，

然后我们发布一下`Taibai.Client`，发布完成以后我们使用ide启动我们的`Taibai.Server`，请注意我们需要使用HTTPS进行启动的，HTTP是不支持H2的！

然后再客户端中打开俩个控制台面板，一个作为监听的monitor，一个作为client进行链接到我们的服务器中。


![](https://img2024.cnblogs.com/blog/2415052/202404/2415052-20240430012918291-1626096563.png)


![](https://img2024.cnblogs.com/blog/2415052/202404/2415052-20240430012922918-1902236194.png)


然后我们使用远程桌面访问我们的`127.0.0.1:60112`，然后我们发现链接成功！如果您跟着写代码您会您发您也成功了，哦耶您获得了一个牛逼的技能，来源于微软MVP token的双休大法的传授！


![](https://img2024.cnblogs.com/blog/2415052/202404/2415052-20240430012928889-372474218.png)


## 技术交流分享

来自微软MVP token

[token | 最有价值专家 (microsoft.com)](https://mvp.microsoft.com/zh-CN/mvp/profile/9dad415c-1eb1-4361-9088-f3dd0d402917)

技术交流群：737776595 

当然如果您需要Demo的代码您可以联系我微信`wk28u9123456789`
