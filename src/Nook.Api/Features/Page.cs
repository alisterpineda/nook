namespace Nook.Api.Features;

/// <summary>One page of a longer list, with the total so a client can show how much is left.</summary>
public record Page<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);
