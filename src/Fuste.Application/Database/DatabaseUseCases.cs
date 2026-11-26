using Fuste.Application.Abstractions;
using Fuste.Application.Common;

namespace Fuste.Application.Database;

public sealed class DatabaseUseCases(IDatabaseRepository database)
{
    public async Task<OperationResult<bool>> ResetAsync(string? confirmation, CancellationToken cancellationToken)
    {
        if (confirmation != "RESET")
        {
            return OperationResult<bool>.Failure("Informe 'RESET' para confirmar a limpeza do banco de dados.");
        }

        await database.ResetAsync(cancellationToken);
        return OperationResult<bool>.Success(true);
    }
}
