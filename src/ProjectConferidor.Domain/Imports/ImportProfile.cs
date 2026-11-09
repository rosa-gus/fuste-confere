namespace ProjectConferidor.Domain.Imports;

public sealed record ImportProfile(Guid Id, Guid CompanyId, string Name, int Revision,
    ImportConfiguration Configuration, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
