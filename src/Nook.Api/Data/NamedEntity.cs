namespace Nook.Api.Data;

/// <summary>
/// A row known by its name and found by that name's key, such as a person, a series or a tag. The setter of the name
/// writes the key, so no writer can forget it.
/// </summary>
public abstract class NamedEntity : Entity
{
    /// <summary>The name people see. Not unique: matching rows by name belongs to ingest.</summary>
    public required string Name
    {
        get;
        set
        {
            field = value;
            NameKey = Data.NameKey.Of(value);
        }
    }

    /// <summary>The name's key, which ingest matches rows on, kept in step by the <see cref="Name"/> setter.</summary>
    public string NameKey { get; private set; } = "";
}
