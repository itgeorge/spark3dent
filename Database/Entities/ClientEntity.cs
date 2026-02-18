namespace Database.Entities;

public class ClientEntity
{
    public string Nickname { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string RepresentativeName { get; set; } = string.Empty;
    public string CompanyIdentifier { get; set; } = string.Empty;
    public string? VatIdentifier { get; set; }
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}
