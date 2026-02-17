using System;
using Invoices;
using NUnit.Framework;
using NUnit.Framework.Constraints;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(BgAmountTranscriber))]
public class BgAmountTranscriberTest
{
    private static BgAmountTranscriber Transcriber => new();

    private static string Transcribe(int amountCents) =>
        Transcriber.Transcribe(new Amount(amountCents, Currency.Eur));

    [Test]
    [TestCase(0, "Нула евро и нула цента")]
    [TestCase(1, "Нула евро и един цент")]
    [TestCase(2, "Нула евро и два цента")]
    [TestCase(3, "Нула евро и три цента")]
    [TestCase(4, "Нула евро и четири цента")]
    [TestCase(5, "Нула евро и пет цента")]
    [TestCase(6, "Нула евро и шест цента")]
    [TestCase(7, "Нула евро и седем цента")]
    [TestCase(8, "Нула евро и осем цента")]
    [TestCase(9, "Нула евро и девет цента")]
    [TestCase(10, "Нула евро и десет цента")]
    [TestCase(11, "Нула евро и единадесет цента")]
    [TestCase(12, "Нула евро и дванадесет цента")]
    [TestCase(13, "Нула евро и тринадесет цента")]
    [TestCase(14, "Нула евро и четиринадесет цента")]
    [TestCase(15, "Нула евро и петнадесет цента")]
    [TestCase(16, "Нула евро и шестнадесет цента")]
    [TestCase(17, "Нула евро и седемнадесет цента")]
    [TestCase(18, "Нула евро и осемнадесет цента")]
    [TestCase(19, "Нула евро и деветнадесет цента")]
    [TestCase(20, "Нула евро и двадесет цента")]
    [TestCase(30, "Нула евро и тридесет цента")]
    [TestCase(40, "Нула евро и четиридесет цента")]
    [TestCase(50, "Нула евро и петдесет цента")]
    [TestCase(60, "Нула евро и шестдесет цента")]
    [TestCase(70, "Нула евро и седемдесет цента")]
    [TestCase(80, "Нула евро и осемдесет цента")]
    [TestCase(90, "Нула евро и деветдесет цента")]
    [TestCase(52, "Нула евро и петдесет и два цента")]
    [TestCase(23, "Нула евро и двадесет и три цента")]
    [TestCase(99, "Нула евро и деветдесет и девет цента")]
    [TestCase(1_00, "Едно евро")]
    [TestCase(2_00, "Две евро")]
    [TestCase(21_00, "Двадесет и едно евро")]
    [TestCase(52_00, "Петдесет и две евро")]
    [TestCase(101_00, "Сто и едно евро")]
    [TestCase(102_00, "Сто и две евро")]
    [TestCase(9_00, "Девет евро")]
    [TestCase(11_00, "Единадесет евро")]
    [TestCase(19_00, "Деветнадесет евро")]
    [TestCase(999_00, "Деветстотин деветдесет и девет евро")]
    [TestCase(999_99, "Деветстотин деветдесет и девет евро и деветдесет и девет цента")]
    [TestCase(1_19, "Едно евро и деветнадесет цента")]
    [TestCase(1_01, "Едно евро и един цент")]
    [TestCase(1_02, "Едно евро и два цента")]
    [TestCase(21_07, "Двадесет и едно евро и седем цента")]
    [TestCase(21_52, "Двадесет и едно евро и петдесет и два цента")]
    [TestCase(52_21, "Петдесет и две евро и двадесет и един цент")]
    [TestCase(123_45, "Сто двадесет и три евро и четиридесет и пет цента")]
    [TestCase(1000_00, "Хиляда евро")]
    [TestCase(1234_56, "Хиляда двеста тридесет и четири евро и петдесет и шест цента")]
    [TestCase(999999_99, "Деветстотин деветдесет и девет хиляди деветстотин деветдесет и девет евро и деветдесет и девет цента")]
    [TestCase(99_99, "Деветдесет и девет евро и деветдесет и девет цента")]
    [TestCase(11_01, "Единадесет евро и един цент")]
    [TestCase(11_02, "Единадесет евро и два цента")]
    [TestCase(11_11, "Единадесет евро и единадесет цента")]
    [TestCase(11_19, "Единадесет евро и деветнадесет цента")]
    [TestCase(19_11, "Деветнадесет евро и единадесет цента")]
    [TestCase(19_19, "Деветнадесет евро и деветнадесет цента")]
    [TestCase(1_11, "Едно евро и единадесет цента")]
    [TestCase(1_12, "Едно евро и дванадесет цента")]
    [TestCase(2_19, "Две евро и деветнадесет цента")]
    [TestCase(10_11, "Десет евро и единадесет цента")]
    [TestCase(21_11, "Двадесет и едно евро и единадесет цента")]
    [TestCase(21_19, "Двадесет и едно евро и деветнадесет цента")]
    [TestCase(100_15, "Сто евро и петнадесет цента")]
    [TestCase(150_19, "Сто и петдесет евро и деветнадесет цента")]
    [TestCase(1_00, "Едно евро")]
    [TestCase(2_00, "Две евро")]
    [TestCase(3_00, "Три евро")]
    [TestCase(4_00, "Четири евро")]
    [TestCase(5_00, "Пет евро")]
    [TestCase(6_00, "Шест евро")]
    [TestCase(7_00, "Седем евро")]
    [TestCase(8_00, "Осем евро")]
    [TestCase(9_00, "Девет евро")]
    [TestCase(10_00, "Десет евро")]
    [TestCase(20_00, "Двадесет евро")]
    [TestCase(30_00, "Тридесет евро")]
    [TestCase(40_00, "Четиридесет евро")]
    [TestCase(50_00, "Петдесет евро")]
    [TestCase(60_00, "Шестдесет евро")]
    [TestCase(70_00, "Седемдесет евро")]
    [TestCase(80_00, "Осемдесет евро")]
    [TestCase(90_00, "Деветдесет евро")]
    [TestCase(100_00, "Сто евро")]
    [TestCase(200_00, "Двеста евро")]
    [TestCase(300_00, "Триста евро")]
    [TestCase(400_00, "Четиристотин евро")]
    [TestCase(500_00, "Петстотин евро")]
    [TestCase(600_00, "Шестстотин евро")]
    [TestCase(700_00, "Седемстотин евро")]
    [TestCase(800_00, "Осемстотин евро")]
    [TestCase(900_00, "Деветстотин евро")]
    [TestCase(110_00, "Сто и десет евро")]
    [TestCase(210_00, "Двеста и десет евро")]
    [TestCase(310_00, "Триста и десет евро")]
    [TestCase(410_00, "Четиристотин и десет евро")]
    [TestCase(510_00, "Петстотин и десет евро")]
    [TestCase(610_00, "Шестстотин и десет евро")]
    [TestCase(710_00, "Седемстотин и десет евро")]
    [TestCase(810_00, "Осемстотин и десет евро")]
    [TestCase(910_00, "Деветстотин и десет евро")]
    [TestCase(1010_00, "Хиляда и десет евро")]
    [TestCase(1020_00, "Хиляда и двадесет евро")]
    [TestCase(1030_00, "Хиляда и тридесет евро")]
    [TestCase(1040_00, "Хиляда и четиридесет евро")]
    [TestCase(1050_00, "Хиляда и петдесет евро")]
    [TestCase(1060_00, "Хиляда и шестдесет евро")]
    [TestCase(1070_00, "Хиляда и седемдесет евро")]
    [TestCase(1080_00, "Хиляда и осемдесет евро")]
    [TestCase(1090_00, "Хиляда и деветдесет евро")]
    [TestCase(1100_00, "Хиляда и сто евро")]
    [TestCase(1200_00, "Хиляда и двеста евро")]
    [TestCase(1300_00, "Хиляда и триста евро")]
    [TestCase(1400_00, "Хиляда и четиристотин евро")]
    [TestCase(1500_00, "Хиляда и петстотин евро")]
    [TestCase(1600_00, "Хиляда и шестстотин евро")]
    [TestCase(1700_00, "Хиляда и седемстотин евро")]
    [TestCase(1800_00, "Хиляда и осемстотин евро")]
    [TestCase(1900_00, "Хиляда и деветстотин евро")]
    [TestCase(10010_00, "Десет хиляди и десет евро")]
    [TestCase(100010_00, "Сто хиляди и десет евро")]
    [TestCase(110110_00, "Сто и десет хиляди сто и десет евро")]
    public void Transcribe_WhenValidAmount_ThenTranscribes(int amountCents, string expected)
    {
        var result = Transcribe(amountCents);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Transcribe_WhenTranscribingAnyNumberBelow1M_ThenTranscribes()
    {
        for (var euros = 1; euros <= 999_999; euros++)
        {
            var cents = euros * 100;
            var result = Transcribe(cents);
            Assert.That(result, Contains.Substring($"евро"));
            if (cents % 100 == 0) 
            {
                Assert.That(result, Does.Not.Contain($"цент"));
            } 
            else 
            {
                Assert.That(result, Contains.Substring($"цент"));
            }

            var getExceptionMessage = () => $"Failed for {cents} cents";
            if (cents / 1_000_00 > 0)
            {
                Assert.That(result.ToLower(), Contains.Substring("хиляда").Or.Contains("хиляди"), getExceptionMessage);
            }
            else if (cents / 100_00 > 0)
            {
                Assert.That(result.ToLower(), Contains.Substring("сто").Or.Contains("ста"), getExceptionMessage);
            }
            else if (cents / 10_00 > 0)
            {
                Assert.That(result.ToLower(), Contains.Substring("десет"), getExceptionMessage);
            }

            Assert.That(result, Is.Not.Null.And.Not.Empty, getExceptionMessage);
        }
    }

    [Test]
    [TestCase(100_000_000)]  // 1,000,000 EUR
    [TestCase(100_000_001)]
    [TestCase(999_999_999)]
    public void Transcribe_WhenTranscribing1MOrAbove_ThenThrows(int amountCents)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Transcribe(amountCents));
    }

    [Test]
    [TestCase(-1)]
    [TestCase(-100)]
    [TestCase(-100_00)]
    public void Transcribe_WhenNegativeAmount_ThenThrows(int amountCents)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Transcribe(amountCents));
    }
}
