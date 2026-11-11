using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<NookDbContext>("nookdb", configureDbContextOptions: options =>
    options.UseSnakeCaseNamingConvention()
        .AddInterceptors(new TimestampInterceptor(TimeProvider.System)));

builder.Services.AddNookAuth(builder.Configuration);

// Enums travel by name, such as "epub", so a client can meet a value it does not know and still show it.
// Controllers and the API description read separate options, and both must agree.
var enumsByName = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(enumsByName));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(enumsByName));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<NookDbContext>().Database.MigrateAsync();
}

// Errors and bare status codes, such as a 401 from the cookie, leave as problem details.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapGet("/", () => "Nook").AllowAnonymous().ExcludeFromDescription();

app.MapControllers();

app.Run();
