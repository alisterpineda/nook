using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<NookDbContext>("nookdb", configureDbContextOptions: options =>
    options.UseSnakeCaseNamingConvention()
        .AddInterceptors(new TimestampInterceptor(TimeProvider.System)));

builder.Services.AddIdentityCore<NookUser>()
    .AddEntityFrameworkStores<NookDbContext>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<NookDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

app.MapGet("/", () => "Nook");

app.Run();
