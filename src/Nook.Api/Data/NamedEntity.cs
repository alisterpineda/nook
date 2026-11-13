namespace Nook.Api.Data;

/// <summary>
/// The one way a row keeps the name and key <see cref="INamed"/> asks for: the setter of the name writes the key, so
/// no writer can forget it.
/// </summary>
public abstract class NamedEntity : Entity, INamed
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
