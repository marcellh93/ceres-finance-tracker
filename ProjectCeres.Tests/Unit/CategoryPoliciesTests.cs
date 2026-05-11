using FluentAssertions;
using ProjectCeres.Models;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

public class CategoryPoliciesTests
{
    [Fact]
    public void Reserved_category_cannot_be_edited()
    {
        var c = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Uncategorized Income",
            IsSystem = false,
            IsReserved = true,
            CategoryTypeId = 1,
            IsActive = true,
        };
        CategoryPolicies.CanEdit(c).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Non_reserved_user_category_can_be_edited()
    {
        var c = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Coffee",
            IsSystem = false,
            IsReserved = false,
            CategoryTypeId = 2,
            IsActive = true,
        };
        CategoryPolicies.CanEdit(c).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void System_category_cannot_be_edited_regardless_of_reserved_flag()
    {
        var c = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Opening Balance",
            IsSystem = true,
            IsReserved = false,
            CategoryTypeId = 1,
            IsActive = true,
        };
        CategoryPolicies.CanEdit(c).IsSuccess.Should().BeFalse();
    }
}
