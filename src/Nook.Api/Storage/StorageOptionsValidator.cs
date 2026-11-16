using Microsoft.Extensions.Options;

namespace Nook.Api.Storage;

/// <summary>Refuses storage settings that put two of Nook's folders in the same place or one inside another, such as a cache inside the library, which would put resized covers among the book files.</summary>
public sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    /// <summary>Fails with a message for every two folders that overlap. A missing data root is left to its own check.</summary>
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DataPath))
        {
            return ValidateOptionsResult.Skip;
        }

        var problems = new NookStorage(Options.Create(options)).FindOverlappingFolders();
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
