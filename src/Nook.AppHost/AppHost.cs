var builder = DistributedApplication.CreateBuilder(args);

var nookDb = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .AddDatabase("nookdb");

// The API keeps its files in .data at the repository root, which git ignores.
var dataPath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".data"));

var api = builder.AddProject<Projects.Nook_Api>("api")
    .WithReference(nookDb)
    .WaitFor(nookDb)
    .WithEnvironment("Nook__DataPath", dataPath)
    .WithHttpHealthCheck("/health");

// The Angular dev server, which proxies /api to the API so the browser sees one origin. It listens on its own
// fixed port, so the endpoint is declared as it is instead of being put behind an Aspire proxy.
builder.AddJavaScriptApp("web", "../Nook.Web", "start")
    .WithNpm()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(port: 4200, isProxied: false);

builder.Build().Run();
