using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Features.Activity;
using Nook.Api.Features.Audit;
using Nook.Api.Features.Auth;
using Nook.Api.Features.Jobs;
using Nook.Api.Features.PublicAccess;
using Nook.Api.Features.Publications;
using Nook.Api.Features.Sections;
using Nook.Api.Features.Security;
using Nook.Api.Features.Uploads;
using Nook.Api.Features.Works;
using Nook.Api.Ingest;
using Nook.Api.Jobs;
using Nook.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

// The build starts the app under GetDocument.Insider to write openapi.json, with no database to migrate,
// no jobs to run and no data folder.
var buildingOpenApiDocument = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

// Settings can also come from nook.json in the data root. It is added first, so everything below reads it.
var settingsFile = buildingOpenApiDocument ? null : builder.Configuration.AddSettingsFile();

builder.AddServiceDefaults();

// One clock for the whole app: the timestamps on saved rows and the times the job worker writes and compares.
var timeProvider = TimeProvider.System;
builder.Services.AddSingleton(timeProvider);

builder.AddNpgsqlDbContext<NookDbContext>("nookdb", configureDbContextOptions: options =>
    options.UseSnakeCaseNamingConvention()
        .AddInterceptors(new TimestampInterceptor(timeProvider)));

// The data root is required, and no folder may sit inside another: the API refuses to start otherwise.
var storageOptions = builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section);
if (!buildingOpenApiDocument)
{
    builder.Services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
    storageOptions.ValidateDataAnnotations().ValidateOnStart();
}
builder.Services.AddSingleton<NookStorage>();

// The page names the hashed scripts and stylesheets of one build, so a browser has to ask again before it reuses it.
// The fallback endpoints read these options too, so this reaches the page however it is asked for.
builder.Services.Configure<StaticFileOptions>(options => options.OnPrepareResponse = context =>
{
    if (string.Equals(context.File.Name, "index.html", StringComparison.OrdinalIgnoreCase))
    {
        context.Context.Response.Headers.CacheControl = "no-cache";
    }
});

// The keys that protect sign-in cookies live in the data folder, so a sign-in outlasts a restart or an upgrade.
// The build has no data folder, so it keeps the default keys.
if (!buildingOpenApiDocument)
{
    builder.Services.AddDataProtection();
    builder.Services.AddOptions<KeyManagementOptions>()
        .Configure<NookStorage, ILoggerFactory>((options, storage, loggers) =>
            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(storage.DataProtectionKeysFolder), loggers));
}

builder.Services.AddSingleton<CoverResizer>();
builder.Services.AddSingleton<CoverCache>();
builder.Services.AddSingleton<PublicationTokens>();
builder.Services.AddSingleton<PublicationCache>();
builder.Services.AddScoped<InstanceSettings>();
builder.Services.AddScoped<SectionAccess>();
builder.Services.AddScoped<AuditLog>();
builder.Services.AddScoped<WorkEditor>();
builder.Services.AddScoped<UploadActivity>();
builder.Services.AddScoped<JobActions>();

builder.Services.AddOptions<UploadOptions>()
    .BindConfiguration(UploadOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddNookAuth(builder.Configuration);
if (!buildingOpenApiDocument)
{
    // Before AddNookJobs, whose no-op default yields to it.
    builder.Services.AddSingleton<IJobChangeNotifier, JobChangeNotifier>();
}

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

// The hub writes enums as the controllers do, so a client reads "unsupportedFormat" and not a number.
builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.Converters.Add(enumsByName);
    options.PayloadSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddSingleton<IUserIdProvider, ActivityUserIdProvider>();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

if (!buildingOpenApiDocument)
{
    // Checked before migrating, so a missing data root or a folder Nook cannot write to stops the API before it
    // touches the database.
    var storageLogger = app.Services.GetRequiredService<ILogger<NookStorage>>();
    if (settingsFile is not null)
    {
        SettingsFile.LogSettingsFile(storageLogger, settingsFile);
    }

    app.Services.GetRequiredService<NookStorage>().PrepareFolders(storageLogger);

    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<NookDbContext>();
    await db.Database.MigrateAsync();

    // A work with no search vector, such as one from before search was built, gets one before Nook answers requests.
    await new WorkSearchIndex(db).RebuildMissingAsync(CancellationToken.None);
}

// Every response carries the web app's policy, an error and the built web app included, unless it sets its own.
app.UseWebAppContentSecurityPolicy();
// And every response tells crawlers not to index it.
app.UseCrawlerDirectives();

// Errors and bare status codes, such as a 401 from the cookie, leave as problem details.
app.UseExceptionHandler();
app.UseStatusCodePages();

// The built web app is public: it holds no data, and its sign-in page must load before anyone has signed in.
app.UseStaticFiles();

app.UseKeptCacheControl();
app.UseAuthentication();
// After authentication, so a limit can tell a signed-in user from a visitor.
app.UseRateLimiter();
app.UseAuthorization();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapControllers();
app.MapHub<ActivityHub>(ActivityHub.Path);
app.MapRobotsTxt();

// The web app routes in the browser, so any other path that is not a file gets its index.html.
// Paths under /api are left out, so an unknown API path still answers as an API. So are the health
// paths, so a probe always gets a health answer and can never pass by getting the web app.
// The regex refuses an empty path, so the root has its own fallback.
app.MapFallbackToFile("/", "index.html").AllowAnonymous().ExcludeFromDescription();
app.MapFallbackToFile("{*path:nonfile:regex(^(?!(api|health|alive)(/|$)))}", "index.html").AllowAnonymous().ExcludeFromDescription();

app.Run();
