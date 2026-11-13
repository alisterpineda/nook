namespace Nook.Api.Data;

/// <summary>A row known by its name, such as a person, a series or a tag.</summary>
public interface INamed
{
    string Name { get; }
}
