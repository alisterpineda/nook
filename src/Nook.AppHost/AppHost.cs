using Microsoft.Extensions.DependencyInjection;
using Nook.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// The database lives in a named volume, so it outlasts the container and stays in step with the files in .data.
var nookDb = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume()
    .AddDatabase("nookdb");

// The API keeps its files in .data at the repository root, which git ignores.
var dataPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".data"));

var api = builder.AddProject<Projects.Nook_Api>("api")
    .WithReference(nookDb)
    .WaitFor(nookDb)
    .WithEnvironment("Nook__DataPath", dataPath)
    .WithHttpHealthCheck("/health")
    // The stack sets Nook up with a known admin, so a dev, a browser test or an agent can sign in on any machine
    // without filling in the setup form. The outcome is logged to the API's console in the dashboard.
    .OnResourceReady(async (resource, evt, cancellationToken) =>
    {
        using var client = new HttpClient { BaseAddress = new Uri(resource.GetEndpoint("http").Url) };
        var logger = evt.Services.GetRequiredService<ResourceLoggerService>().GetLogger(resource);
        await DevAdmin.EnsureAsync(client, logger, cancellationToken);
    });

// The Angular dev server, which proxies /api to the API so the browser sees one origin. It listens on its own
// fixed port, so the endpoint is declared as it is instead of being put behind an Aspire proxy.
builder.AddJavaScriptApp("web", "../Nook.Web", "start")
    .WithNpm()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 4200, isProxied: false);

builder.Build().Run();
