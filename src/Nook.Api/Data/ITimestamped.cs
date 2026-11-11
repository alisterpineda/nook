namespace Nook.Api.Data;

/// <summary>A row with created and updated times in UTC, kept by <see cref="TimestampInterceptor"/>.</summary>
public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}
