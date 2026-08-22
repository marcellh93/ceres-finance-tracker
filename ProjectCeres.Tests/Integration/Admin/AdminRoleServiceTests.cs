using FluentAssertions;
using ProjectCeres.Common;

namespace ProjectCeres.Tests.Integration.Admin;

public class AppRolesTests
{
    [Fact]
    public void Admin_role_name_is_the_exact_string_used_by_authorize_attributes()
    {
        AppRoles.Admin.Should().Be("Admin",
            "the value is duplicated in [Authorize(Roles = \"Admin\")] attributes, " +
            "which take a literal string and cannot reference the constant");
    }
}
