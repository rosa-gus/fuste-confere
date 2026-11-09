namespace ProjectConferidor.Domain.Companies;

public sealed record RepresentedCompany(Guid Id, string Name, DateTimeOffset CreatedAt)
{
    public static RepresentedCompany Create(string name)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is 0 or > 160)
        {
            throw new ArgumentException("Company name must contain between 1 and 160 characters.", nameof(name));
        }

        return new RepresentedCompany(Guid.NewGuid(), normalizedName, DateTimeOffset.UtcNow);
    }
}
