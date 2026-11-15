using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Publications;
using Nook.Api.Features.Uploads;
using Nook.Api.Features.Works;
using Nook.Api.Ingest;
using Nook.Api.Jobs;
using Nook.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

// The build starts the app under GetDocument.Insider to write openapi.json, with no database to migrate,
// no jobs to run and no data folder.
var buildingOpenApiDocument = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

builder.AddServiceDefaults();

// One clock for the whole app: the timestamps on saved rows and the times the job worker writes and compares.
var timeProvider = TimeProvider.System;
builder.Services.AddSingleton(timeProvider);

builder.AddNpgsqlDbContext<NookDbContext>("nookdb", configureDbContextOptions: options =>
    options.UseSnakeCaseNamingConvention()
        .AddInterceptors(new TimestampInterceptor(timeProvider)));

// The data root is required: the API refuses to start without it.
var storageOptions = builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section);
if (!buildingOpenApiDocument)
{
    storageOptions.ValidateDataAnnotations().ValidateOnStart();
}
builder.Services.AddSingleton<NookStorage>();
builder.Services.AddSingleton<CoverResizer>();
builder.Services.AddSingleton<CoverCache>();
builder.Services.AddSingleton<PublicationTokens>();
builder.Services.AddSingleton<PublicationCache>();

builder.Services.AddOptions<UploadOptions>()
    .BindConfiguration(UploadOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddNookAuth(builder.Configuration);
builder.Services.AddNookJobs();
builder.Services.AddNookIngest();
if (!buildingOpenApiDocument)
{
    builder.Services.AddJobWorker();
}

// Enums travel by name, such as "epub", so a client can meet a value it does not know and still show it.
// Numbers are numbers only: the web default also reads them from strings, and the API description
// would then type every number as "integer or string".
// Controllers and the API description read separate options, and both must agree.
var enumsByName = new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(enumsByName);
    options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(enumsByName);
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (!buildingOpenApiDocument)
{
    // Checked before migrating, so a missing data root stops the API before it touches the database.
    app.Services.GetRequiredService<NookStorage>().CreateFolders();

    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NookDbContext>().Database.MigrateAsync();
}

// Errors and bare status codes, such as a 401 from the cookie, leave as problem details.
app.UseExceptionHandler();
app.UseStatusCodePages();

// The built web app is public: it holds no data, and its sign-in page must load before anyone has signed in.
app.UseStaticFiles();

app.UseRateLimiter();
app.UseKeptCacheControl();
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();

// The web app routes in the browser, so any other path that is not a file gets its index.html.
// Paths under /api are left out, so an unknown API path still answers as an API. So are the health
// paths, which are mapped only in development: elsewhere a probe must not pass by getting the web app.
// The regex refuses an empty path, so the root has its own fallback.
app.MapFallbackToFile("/", "index.html").AllowAnonymous().ExcludeFromDescription();
app.MapFallbackToFile("{*path:nonfile:regex(^(?!(api|health|alive)(/|$)))}", "index.html").AllowAnonymous().ExcludeFromDescription();

app.Run();
