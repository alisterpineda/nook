using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Nook.Api.Features.Auth;

/// <summary>How a refusal from ASP.NET Core Identity reaches the form that asked.</summary>
public static class IdentityErrors
{
    /// <summary>
    /// Files each error of a refused account under the field it is about: one whose code starts with <c>Password</c>
    /// under <paramref name="passwordField"/>, and any other, which is about the email or the user name made from it,
    /// under <paramref name="emailField"/>.
    /// </summary>
    public static void AddAccountErrors(this ModelStateDictionary modelState, IdentityResult result, string passwordField, string emailField)
    {
        foreach (var error in result.Errors)
        {
            var field = error.Code.StartsWith("Password", StringComparison.Ordinal) ? passwordField : emailField;
            modelState.AddModelError(field, error.Description);
        }
    }
}
