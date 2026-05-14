using Microsoft.Extensions.Logging;

namespace ProjectCeres.Common;

public sealed class BackgroundJobScope(IUserScope scope, ILogger<BackgroundJobScope> logger)
    : IBackgroundJobScope
{
    public async Task RunAsync(Guid userId, string jobName, Func<Task> work)
    {
        if (userId == Guid.Empty)
        {
            logger.LogError("Background job refused: no user declared. Job: {JobName}", jobName);
            throw new InvalidOperationException(
                "Background jobs must declare a user. Call RunAsync with a non-empty user id.");
        }

        using var _ = scope.EnterAs(userId);
        await work().ConfigureAwait(false);
    }
}
