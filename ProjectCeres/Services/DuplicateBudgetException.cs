namespace ProjectCeres.Services;

/// <summary>
/// Thrown when a CategoryBudget Create or Reactivate would violate the
/// unique-active-per-(Category, Currency) constraint. Carries the existing
/// budget's id and active state so the API layer can return a 409 with
/// enough info for the SPA to offer a one-click reactivate.
/// </summary>
public sealed class DuplicateBudgetException(Guid existingBudgetId, bool existingIsActive)
    : Exception("A budget for this category and currency already exists.")
{
    public Guid ExistingBudgetId { get; } = existingBudgetId;
    public bool ExistingIsActive { get; } = existingIsActive;
}
