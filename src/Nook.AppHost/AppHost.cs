var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Nook_Api>("api")
    .WithHttpHealthCheck("/health");

builder.Build().Run();
