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