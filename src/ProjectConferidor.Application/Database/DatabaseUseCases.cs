using ProjectConferidor.Application.Abstractions;
using ProjectConferidor.Application.Common;

namespace ProjectConferidor.Application.Database;

public sealed class DatabaseUseCases(IDatabaseRepository database)
{
    public async Task<OperationResult<bool>> ResetAsync(string? confirmation, CancellationToken cancellationToken)
    {
        if (confirmation != "RESET")
        {
            return OperationResult<bool>.Failure("Provide 'RESET' as confirmation to reset the database.");
        }

        await database.ResetAsync(cancellationToken);
        return OperationResult<bool>.Success(true);
    }
}
