using System.ComponentModel.DataAnnotations;
using Nook.Api.Data;
using Nook.Api.Features.Works;

namespace Nook.Api.Features.People;

public class PersonListQuery
{
    /// <summary>Only people who hold this role on a work, with only those works counted.</summary>
    public ContributorRole? Role { get; set; }

    [Range(0, int.MaxValue)]
    public int Offset { get; set; }

    [Range(1, 200)]
    public int Limit { get; set; } = 50;
}

/// <summary>A person as a list shows them, with how many works they are credited on.</summary>
public record PersonSummaryResponse(Guid Id, string Name, string SortName, int WorkCount);

/// <summary>One role a person holds, with each work they hold it on as a list shows it.</summary>
public record PersonRoleResponse(ContributorRole Role, IReadOnlyList<WorkSummaryResponse> Works);

/// <summary>A person with the works they are credited on, grouped by what they did on them.</summary>
public record PersonDetailResponse(Guid Id, string Name, string SortName, IReadOnlyList<PersonRoleResponse> Roles);
