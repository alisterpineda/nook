using Nook.Api.Ingest.Epub;
using Nook.Api.Jobs;

namespace Nook.Api.Ingest;

public static class IngestServiceExtensions
{
    /// <summary>Adds the ingest job type and the formats it reads. Call it after <see cref="JobServiceExtensions.AddNookJobs"/>.</summary>
    public static IServiceCollection AddNookIngest(this IServiceCollection services)
    {
        services.AddJobType<IngestJob>();
        services.AddSingleton<IFormatHandler, EpubFormatHandler>();
        return services;
    }
}
