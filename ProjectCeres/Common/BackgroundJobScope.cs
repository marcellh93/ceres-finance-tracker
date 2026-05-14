using Microsoft.Extensions.Logging;

namespace ProjectCeres.Common;

public sealed class BackgroundJobScope(
    ICurrentUserAccessor currentUser,
    IUserScope scope,
    ILogger<BackgroundJobScope> logger)
    : IBackgroundJobScope
{
    public async Task RunAsync(Guid userId, string jobName, Func<Task> work)
    {
        if (userId == Guid.Empty)
        {
            // Stage 7.6.7 / ADR-0073: name the rejected case in the diagnostic. The caller
            // tried to enter a background scope without declaring a user; the surrounding
            // context tells us where the call came from.
            var actualCase = currentUser.Context.GetType().Name;
            logger.LogError(
                "Background job refused: no user declared. Job: {JobName}. Caller context: {Context}",
                jobName, actualCase);
            throw new InvalidOperationException(
                $"Background jobs must declare a user. Call RunAsync with a non-empty user id. " +
                $"Caller context was: {actualCase}.");
        }

        using var _ = scope.EnterAs(userId);
        await work().ConfigureAwait(false);
    }
}
