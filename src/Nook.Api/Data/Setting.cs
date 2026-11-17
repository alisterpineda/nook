namespace Nook.Api.Data;

/// <summary>One setting of this Nook instance, kept in the database so an admin can change it in the app.</summary>
public class Setting : Entity
{
    /// <summary>Which setting this is. Each key has at most one row.</summary>
    public required string Key { get; set; }

    /// <summary>The setting's value as text; the code that reads the key gives it meaning.</summary>
    public required string Value { get; set; }
}
