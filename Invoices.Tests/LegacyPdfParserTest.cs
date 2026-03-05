using System;
using System.Globalization;
using System.IO;
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

    /// <summary>
    /// Some invoices use the EUR symbol (€) instead of "евро" text.
    /// </summary>
    [Test]
    public void TryParseFromText_WhenAmountWithEurSymbol_ThenExtractsEur()
    {
        var text = """
            Номер 250
            Дата 26.02.2026г.
            Получател Тест ЕООД : Адрес: ул.Тест 1 гр.София
            ЕИК по Булстат: 205263288
            Сума за плащане: 560.00 €
            """;
        var result = LegacyPdfParser.TryParseFromText(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Number, Is.EqualTo("250"));
        Assert.That(result.TotalCents, Is.EqualTo(56000));
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

    /// <summary>
    /// Legacy invoices (e.g. Dr. Milanova, Dr. Stoichev) sometimes use "ЕИК:" instead of "ЕИК по Булстат:".
    /// </summary>
    [Test]
    public void TryParseFromText_WhenEikWithoutBulstatLabel_ThenExtractsRecipient()
    {
        var text = """
            Номер 18
            Дата 18.01.2022г.
            Получател д-р Миланова ЕООД :
            Адрес: гр.София, ул.Примерна 5
            ЕИК: 123456789
            МОЛ: д-р Мария Миланова
            Сума за плащане: 150.00 лв.
            """;
        var result = LegacyPdfParser.TryParseFromText(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Number, Is.EqualTo("18"));
        Assert.That(result.Date, Is.EqualTo(new DateTime(2022, 1, 18)));
        Assert.That(result.TotalCents, Is.EqualTo(15000));
        Assert.That(result.Recipient.Name, Does.Contain("Миланова"));
        Assert.That(result.Recipient.CompanyIdentifier, Is.EqualTo("123456789"));
        Assert.That(result.Recipient.RepresentativeName, Does.Contain("Миланова"));
    }

    /// <summary>
    /// Some invoices use "Мол" instead of "МОЛ" (mixed case).
    /// </summary>
    [Test]
    public void TryParseFromText_WhenMolLabelMixedCase_ThenExtractsRepresentativeName()
    {
        var text = """
            Номер 7
            Дата 15.03.2024г.
            Получател Тест ООД : Адрес: ул.Тест 1 гр.София
            ЕИК по Булстат: 111222333
            Мол: д-р Петър Петров
            Сума за плащане: 100.00 лв.
            """;
        var result = LegacyPdfParser.TryParseFromText(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Recipient.RepresentativeName, Is.EqualTo("д-р Петър Петров"));
    }

    /// <summary>
    /// When МОЛ label is missing, detect 2 Cyrillic names with "д-р" prefix after the ЕИК number as the representative.
    /// </summary>
    [Test]
    public void TryParseFromText_WhenMolLabelMissingButTwoCyrillicNamesWithDoctorPrefixAfterEik_ThenExtractsAsRepresentative()
    {
        var text = """
            Номер 14
            Дата 30.11.2021г.
            Получател ГППДП "Тест Клиник" ООД :
            Адрес: ул.Проба 22 гр.Пловдив
            ЕИК по Булстат: 987654321
            д-р Мария Проба
            Сума за плащане: 190.00 лв.
            """;
        var result = LegacyPdfParser.TryParseFromText(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Recipient.CompanyIdentifier, Is.EqualTo("987654321"));
        Assert.That(result.Recipient.RepresentativeName, Is.EqualTo("д-р Мария Проба"));
    }

    /// <summary>
    /// When МОЛ label is missing, detect 2 Cyrillic names after the ЕИК number as the representative.
    /// </summary>
    [Test]
    public void TryParseFromText_WhenMolLabelMissingButTwoCyrillicNamesAfterEik_ThenExtractsAsRepresentative()
    {
        var text = """
            Номер 14
            Дата 30.11.2021г.
            Получател ГППДП "Тест Клиник" ООД :
            Адрес: ул.Проба 22 гр.Пловдив
            ЕИК по Булстат: 987654321
            Мария Проба
            Сума за плащане: 190.00 лв.
            """;
        var result = LegacyPdfParser.TryParseFromText(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Recipient.CompanyIdentifier, Is.EqualTo("987654321"));
        Assert.That(result.Recipient.RepresentativeName, Is.EqualTo("Мария Проба"));
    }

    /// <summary>
    /// Same as above but with 3 unlabeled Cyrillic names after ЕИК (e.g. д-р + first + last).
    /// </summary>
    [Test]
    public void TryParseFromText_WhenMolLabelMissingButThreeCyrillicNamesAfterEik_ThenExtractsAsRepresentative()
    {
        var text = """
            Номер 22
            Дата 10.05.2023г.
            Получател Клиника ООД : Адрес: ул.Здраве 5 гр.София
            ЕИК по Булстат: 555666777
            д-р Иван Петров Иванов
            Сума за плащане: 300.00 лв.
            """;
        var result = LegacyPdfParser.TryParseFromText(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Recipient.CompanyIdentifier, Is.EqualTo("555666777"));
        Assert.That(result.Recipient.RepresentativeName, Is.EqualTo("д-р Иван Петров Иванов"));
    }

    /// <summary>
    /// Same variant: "ЕИК" without "по Булстат" — different invoice layout.
    /// </summary>
    [Test]
    public void TryParseFromText_WhenEikShortLabelOnly_ThenExtractsAllFields()
    {
        var text = """
            Номер 42
            Дата 04.02.2026г.
            Получател д-р Стоичев ООД : Адрес: ул.Тест 10 гр.Пловдив
            ЕИК 987654321
            Сума за плащане: 200.00 лв.
            """;
        var result = LegacyPdfParser.TryParseFromText(text);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Number, Is.EqualTo("42"));
        Assert.That(result.Date, Is.EqualTo(new DateTime(2026, 2, 4)));
        Assert.That(result.TotalCents, Is.EqualTo(20000));
        Assert.That(result.Recipient.CompanyIdentifier, Is.EqualTo("987654321"));
    }

    /// <summary>
    /// Uncomment to run a temporary test: parses a real PDF file and asserts expected values. Skip if file not found.
    /// </summary>
    [TestCase("D:/Dropbox/Quick Uploads/1772718915306178.pdf", 56000, Currency.Eur, "205263288", "250", "26.02.2026")]
    public void TryParse_WhenRealPdfFile_ThenExtractsExpectedValues(
        string filePathOrUri,
        int expectedTotalCents,
        Currency expectedCurrency,
        string expectedCompanyNumber,
        string expectedInvoiceNumber,
        string expectedDateStr)
    {
        var path = filePathOrUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(filePathOrUri).LocalPath
            : filePathOrUri;

        if (!File.Exists(path))
        {
            Assert.Ignore($"File not found: {path}");
            return;
        }

        var bytes = File.ReadAllBytes(path);
        var result = LegacyPdfParser.TryParse(bytes);

        Assert.That(result, Is.Not.Null, "PDF should parse successfully");
        Assert.That(result!.TotalCents, Is.EqualTo(expectedTotalCents), "Total amount mismatch");
        Assert.That(result.Currency, Is.EqualTo(expectedCurrency), "Currency mismatch");
        Assert.That(result.Recipient.CompanyIdentifier, Is.EqualTo(expectedCompanyNumber), "Company number mismatch");
        Assert.That(result.Number, Is.EqualTo(expectedInvoiceNumber), "Invoice number mismatch");

        var expectedDate = DateTime.ParseExact(expectedDateStr, "dd.MM.yyyy", CultureInfo.InvariantCulture);
        Assert.That(result.Date, Is.EqualTo(expectedDate), "Invoice date mismatch");
    }
}
