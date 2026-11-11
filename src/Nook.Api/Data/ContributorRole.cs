namespace Nook.Api.Data;

/// <summary>What a person did on a work. Stored as the member name, so new members need no migration.</summary>
public enum ContributorRole
{
    Author,
    Narrator,
    Translator,
    Editor,
}
