using Accounting;
using Invoices;

namespace Database;

public static class ClientMapping
{
    public static Client ToDomain(Entities.ClientEntity entity)
    {
        var address = new BillingAddress(
            entity.Name,
            entity.RepresentativeName,
            entity.CompanyIdentifier,
            entity.VatIdentifier,
            entity.Address,
            entity.City,
            entity.PostalCode,
            entity.Country);
        return new Client(entity.Nickname, address);
    }

    public static Entities.ClientEntity ToEntity(Client client)
    {
        return new Entities.ClientEntity
        {
            Nickname = client.Nickname,
            Name = client.Address.Name,
            RepresentativeName = client.Address.RepresentativeName,
            CompanyIdentifier = client.Address.CompanyIdentifier,
            VatIdentifier = client.Address.VatIdentifier,
            Address = client.Address.Address,
            City = client.Address.City,
            PostalCode = client.Address.PostalCode,
            Country = client.Address.Country
        };
    }
}
