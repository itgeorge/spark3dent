using Invoices;

namespace Database;

public static class InvoiceMapping
{
    public static Invoice ToDomain(Entities.InvoiceEntity entity)
    {
        var seller = new BillingAddress(
            entity.SellerName,
            entity.SellerRepresentativeName,
            entity.SellerCompanyIdentifier,
            entity.SellerVatIdentifier,
            entity.SellerAddress,
            entity.SellerCity,
            entity.SellerPostalCode,
            entity.SellerCountry);
        var buyer = new BillingAddress(
            entity.BuyerName,
            entity.BuyerRepresentativeName,
            entity.BuyerCompanyIdentifier,
            entity.BuyerVatIdentifier,
            entity.BuyerAddress,
            entity.BuyerCity,
            entity.BuyerPostalCode,
            entity.BuyerCountry);
        var bank = new BankTransferInfo(entity.BankIban, entity.BankName, entity.BankBic);
        var lineItems = entity.LineItems
            .OrderBy(li => li.Id)
            .Select(li => new Invoice.LineItem(li.Description, new Amount(li.AmountCents, Enum.Parse<Currency>(li.Currency))))
            .ToArray();

        var content = new Invoice.InvoiceContent(
            entity.Date,
            seller,
            buyer,
            lineItems,
            bank);

        return new Invoice(entity.Number, content);
    }

    public static void ApplyContent(Entities.InvoiceEntity entity, Invoice.InvoiceContent content)
    {
        entity.Date = content.Date;

        entity.SellerName = content.SellerAddress.Name;
        entity.SellerRepresentativeName = content.SellerAddress.RepresentativeName;
        entity.SellerCompanyIdentifier = content.SellerAddress.CompanyIdentifier;
        entity.SellerVatIdentifier = content.SellerAddress.VatIdentifier;
        entity.SellerAddress = content.SellerAddress.Address;
        entity.SellerCity = content.SellerAddress.City;
        entity.SellerPostalCode = content.SellerAddress.PostalCode;
        entity.SellerCountry = content.SellerAddress.Country;

        entity.BuyerName = content.BuyerAddress.Name;
        entity.BuyerRepresentativeName = content.BuyerAddress.RepresentativeName;
        entity.BuyerCompanyIdentifier = content.BuyerAddress.CompanyIdentifier;
        entity.BuyerVatIdentifier = content.BuyerAddress.VatIdentifier;
        entity.BuyerAddress = content.BuyerAddress.Address;
        entity.BuyerCity = content.BuyerAddress.City;
        entity.BuyerPostalCode = content.BuyerAddress.PostalCode;
        entity.BuyerCountry = content.BuyerAddress.Country;

        entity.BankIban = content.BankTransferInfo.Iban;
        entity.BankName = content.BankTransferInfo.BankName;
        entity.BankBic = content.BankTransferInfo.Bic;

        entity.LineItems.Clear();
        foreach (var li in content.LineItems)
        {
            entity.LineItems.Add(new Entities.InvoiceLineItemEntity
            {
                Description = li.Description,
                AmountCents = li.Amount.Cents,
                Currency = li.Amount.Currency.ToString()
            });
        }
    }

    public static Entities.InvoiceEntity ToEntity(Invoice.InvoiceContent content)
    {
        var entity = new Entities.InvoiceEntity();
        ApplyContent(entity, content);
        return entity;
    }

    public static void SetNumber(Entities.InvoiceEntity entity, string number)
    {
        entity.Number = number;
        entity.NumberNumeric = long.Parse(number);
    }
}
