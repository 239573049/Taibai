using Taibai.Server;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions());

builder.Host.ConfigureHostOptions(host => { host.ShutdownTimeout = TimeSpan.FromSeconds(1d); });

// builder.Services.AddSingleton<ClientMiddleware>();
builder.Services.AddSingleton<ServerMiddleware>();
builder.Services.AddSingleton<HttpTunnelFactory>();
builder.Services.AddSingleton<TunnelMiddleware>();
builder.Services.AddSingleton<ClientManager>();
builder.Services.AddSingleton<ClientStateChannel>();
builder.Services.AddSingleton<LocalClientMiddleware>();

var app = builder.Build();
//
// app.Map("/server", app =>
// {
//     app.Use(Middleware);
//
//     static async Task Middleware(HttpContext context, RequestDelegate _)
//     {
//         await ServerService.StartAsync(context);
//     }
// });
//
app.Map("/client", app =>
{
    app.UseMiddleware<LocalClientMiddleware>();
});



app.Map("/server", app =>
{
    app.UseMiddleware<ServerMiddleware>();
    app.UseMiddleware<TunnelMiddleware>();
});


app.Run();