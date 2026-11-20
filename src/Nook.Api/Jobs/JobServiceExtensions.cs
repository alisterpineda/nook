using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nook.Api.Jobs;

public static class JobServiceExtensions
{
    /// <summary>Adds the job queue, the registry of job types and the worker's options. The worker itself is added by <see cref="AddJobWorker"/>.</summary>
    public static IServiceCollection AddNookJobs(this IServiceCollection services)
    {
        services.AddOptions<JobsOptions>()
            .BindConfiguration(JobsOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton<JobTypeRegistry>();
        services.TryAddSingleton<JobSignal>();
        services.TryAddSingleton<IJobChangeNotifier, NoJobChangeNotifier>();
        services.TryAddScoped<JobQueue>();
        services.TryAddScoped<JobDeletion>();
        return services;
    }

    /// <summary>
    /// Runs jobs in this process, in the background, with as many loops as <see cref="JobsOptions.WorkerCount"/> says.
    /// Call it after <see cref="AddNookJobs"/>.
    /// </summary>
    public static IServiceCollection AddJobWorker(this IServiceCollection services)
    {
        services.AddHostedService<JobWorker>();
        return services;
    }

    /// <summary>Registers a job type, as a singleton. Registering the same type twice has no further effect.</summary>
    public static IServiceCollection AddJobType<T>(this IServiceCollection services)
        where T : JobType
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<JobType, T>());
        return services;
    }
}
