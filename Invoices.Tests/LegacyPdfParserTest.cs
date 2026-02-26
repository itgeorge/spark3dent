using System;
using Invoices;
using NUnit.Framework;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(LegacyPdfParser))]
public class LegacyPdfParserTest
{
    private const string SampleText = """
        Тест Продавач ООД ФАКТУРА Оригинал
        Номер 0000000106
        Дата 01.02.2024г.
        Получател "Тест Дент Студио" ЕООД :
        Адрес: гр.София, ул.Тестова 1
        ЕИК по Булстат: 123456789
        МОЛ: Иван Тестов
        № Описание на стоката/услугата Сума 270.00 лв.
        1 Зъботехнически услуги
        Сума за плащане: 270.00 лв.
        """;

    private const string SampleTextIvanRilski = """
        Номер 0000000014
        Дата 30.11.2021г.
        Получател ГППДП "Тест Клиник" ООД :
        Адрес: ул.Проба 22 гр.Пловдив
        ЕИК по Булстат: 987654321
        д-р Мария Проба
        Сума за плащане: 190.00 лв.
        """;

    [Test]
    public void TryParseFromText_WhenValidStandardFormat_ThenExtractsAllFields()
    {
        var result = LegacyPdfParser.TryParseFromText(SampleText);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Number, Is.EqualTo("106"));
        Assert.That(result.Date, Is.EqualTo(new DateTime(2024, 2, 1)));
        Assert.That(result.TotalCents, Is.EqualTo(27000));
        Assert.That(result.Currency, Is.EqualTo(Currency.Bgn));
        Assert.That(result.Recipient.Name, Does.Contain("Тест").And.Contain("Дент"));
        Assert.That(result.Recipient.CompanyIdentifier, Is.EqualTo("123456789"));
        Assert.That(result.Recipient.RepresentativeName, Is.EqualTo("Иван Тестов"));
        Assert.That(result.Recipient.Address, Does.Contain("София").Or.Contain("Тестова"));
        Assert.That(result.Recipient.City, Is.EqualTo("София"));
    }

    [Test]
    public void TryParseFromText_WhenValidFormatWithoutMolLabel_ThenExtractsAllFields()
    {
        var result = LegacyPdfParser.TryParseFromText(SampleTextIvanRilski);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Number, Is.EqualTo("14"));
        Assert.That(result.Date, Is.EqualTo(new DateTime(2021, 11, 30)));
        Assert.That(result.TotalCents, Is.EqualTo(19000));
        Assert.That(result.Currency, Is.EqualTo(Currency.Bgn));
        Assert.That(result.Recipient.Name, Does.Contain("Тест"));
        Assert.That(result.Recipient.CompanyIdentifier, Is.EqualTo("987654321"));
        Assert.That(result.Recipient.Address, Does.Contain("Проба"));
        Assert.That(result.Recipient.City, Is.EqualTo("Пловдив"));
    }

    [Test]
    public void TryParseFromText_WhenAmountInEuros_ThenExtractsEur()
    {
        var text = """
            Номер 42
            Дата 15.06.2024г.
            Получател Тест ЕООД : Адрес: ул.Тест 1 гр.София
            ЕИК по Булстат: 111222333
            Сума за плащане: 320.00 евро
            """;
        var result = LegacyPdfParser.TryParseFromText(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Number, Is.EqualTo("42"));
        Assert.That(result.TotalCents, Is.EqualTo(32000));
        Assert.That(result.Currency, Is.EqualTo(Currency.Eur));
    }

    [Test]
    public void TryParseFromText_WhenMissingPoluchatel_ThenReturnsNull()
    {
        var text = "Номер 1 Дата 01.01.2024г. Сума за плащане: 100.00 лв.";
        var result = LegacyPdfParser.TryParseFromText(text);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryParseFromText_WhenMissingEik_ThenReturnsNull()
    {
        var text = """
            Номер 1 Дата 01.01.2024г.
            Получател Test EOOD : Адрес: ул. Test 1 гр. София
            Сума за плащане: 100.00 лв.
            """;
        var result = LegacyPdfParser.TryParseFromText(text);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryParseFromText_WhenInvalidDate_ThenReturnsNull()
    {
        var text = """
            Номер 1
            Получател Test EOOD : Адрес: ул. Test 1 гр. София
            ЕИК по Булстат: 123456789
            Сума за плащане: 100.00 лв.
            """;
        var result = LegacyPdfParser.TryParseFromText(text);
        Assert.That(result, Is.Null);
    }
}
