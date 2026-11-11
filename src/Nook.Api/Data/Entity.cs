namespace Nook.Api.Data;

/// <summary>A Nook row: a version 7 UUID key, and created and updated times kept by <see cref="TimestampInterceptor"/>.</summary>
public abstract class Entity : ITimestamped
{
    protected Entity() => Id = Guid.CreateVersion7();

    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
