namespace Nook.Api.Data;

/// <summary>One copy of a work in one format, made of one or more parts. A work can hold several items of the same format.</summary>
public class Item : Entity
{
    public Guid WorkId { get; set; }

    public ItemFormat Format { get; set; }
}
