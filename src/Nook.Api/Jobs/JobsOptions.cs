using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Jobs;

/// <summary>How the job worker runs, bound from <c>Nook:Jobs</c>.</summary>
public sealed class JobsOptions
{
    public const string Section = "Nook:Jobs";

    /// <summary>How many jobs run at once. Zero runs none, so jobs stay pending.</summary>
    [Range(0, int.MaxValue)]
    public int WorkerCount { get; set; } = 1;

    /// <summary>How long a worker holds a job it has claimed, in minutes, from a few milliseconds to a day. Fractions are allowed.</summary>
    [Range(0.0001, 1440)]
    public double LeaseMinutes { get; set; } = 5;

    /// <summary>How often an idle worker looks for a job, in seconds, from a hundredth of one to an hour. Fractions are allowed.</summary>
    [Range(0.01, 3600)]
    public double PollSeconds { get; set; } = 5;
}
