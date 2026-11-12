namespace ProjectConferidor.Domain.Companies;

public sealed record RepresentedCompany(Guid Id, string Name, DateTimeOffset CreatedAt)
{
    public static RepresentedCompany Create(string name)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is 0 or > 160)
        {
            throw new ArgumentException("O nome da representada deve ter entre 1 e 160 caracteres.");
        }

        return new RepresentedCompany(Guid.NewGuid(), normalizedName, DateTimeOffset.UtcNow);
    }
}
