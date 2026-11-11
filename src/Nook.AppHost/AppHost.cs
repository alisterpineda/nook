var builder = DistributedApplication.CreateBuilder(args);

var nookDb = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .AddDatabase("nookdb");

builder.AddProject<Projects.Nook_Api>("api")
    .WithReference(nookDb)
    .WaitFor(nookDb)
    .WithHttpHealthCheck("/health");

builder.Build().Run();
