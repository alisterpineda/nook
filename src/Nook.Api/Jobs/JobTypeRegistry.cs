namespace Nook.Api.Jobs;

/// <summary>The registered job types, found by the name stored on each job.</summary>
public sealed class JobTypeRegistry
{
    private readonly Dictionary<string, JobType> _types;

    public JobTypeRegistry(IEnumerable<JobType> types)
    {
        _types = new Dictionary<string, JobType>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (!_types.TryAdd(type.Name, type))
            {
                throw new InvalidOperationException(
                    $"Job types {_types[type.Name].GetType().Name} and {type.GetType().Name} share the name '{type.Name}'.");
            }
        }
    }

    /// <summary>The type with this name, or null when none is registered.</summary>
    public JobType? Find(string name) => _types.GetValueOrDefault(name);

    /// <summary>
    /// How many runs a job of this type may make. A type that is not registered gets one, so its job is stored,
    /// runs once and fails where it can be seen.
    /// </summary>
    public int AttemptsFor(string name) => Find(name)?.Attempts ?? 1;
}
