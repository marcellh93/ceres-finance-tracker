using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ProjectCeres.Tests.Integration.Authentication;

public class ArchitectureTests
{
    private static readonly Assembly App = typeof(global::Program).Assembly;

    [Fact]
    public void No_api_controller_class_has_AllowAnonymous()
    {
        // Legacy Razor controllers under Controllers/* (not Controllers/Api/*) are
        // permitted class-level AllowAnonymous because they are SPA-shell or 302-redirect
        // holdovers slated for deletion in Batch 4. New API controllers must declare
        // anonymity per-method to make the choice explicit and reviewable.
        var violations = App.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.Namespace?.Contains(".Api") == true)
            .Where(t => t.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
            .Select(t => t.FullName!)
            .ToList();

        violations.Should().BeEmpty(
            "API controllers must declare AllowAnonymous at the method level only — class-level lets new actions inherit anonymity by accident");
    }

    [Fact]
    public void Every_controller_action_declares_authorization_intent()
    {
        var violations = App.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .Where(m => m.GetCustomAttribute<AuthorizeAttribute>() is null
                     && m.GetCustomAttribute<AllowAnonymousAttribute>() is null
                     && m.DeclaringType!.GetCustomAttribute<AuthorizeAttribute>() is null)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        violations.Should().BeEmpty(
            "explicit [Authorize] or [AllowAnonymous] required on every action — global fallback hides the intent");
    }

    [Fact]
    public void Api_HttpGet_actions_must_not_have_write_verb_names()
    {
        // Legacy Razor controllers under Controllers/* are 302-redirect holdovers
        // whose action names mirror legacy URL paths (e.g. Edit, Create, Confirm,
        // Dispute) but the action body is a Redirect — not a state change. They are
        // slated for deletion in Batch 4. The API surface is where naming hygiene
        // actually maps to behaviour.
        var forbiddenPrefixes = new[]
        {
            "Create", "Update", "Delete", "Remove",
            "Archive", "Deactivate", "Disable", "Enable", "Restore",
            "Reset", "Submit", "Send", "Process",
            "Approve", "Reject", "Confirm", "Dispute", "Cancel",
            "Upload", "Set"
        };

        var violations = App.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .Where(t => t.Namespace?.Contains(".Api") == true)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<HttpGetAttribute>() is not null)
            .Where(m => forbiddenPrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        violations.Should().BeEmpty("API GET endpoints must be side-effect-free per RFC 9110");
    }
}
