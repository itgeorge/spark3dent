using Invoices;
using NUnit.Framework;

namespace Invoices.Tests;

[TestFixture]
[TestOf(typeof(BgAmountTranscriber))]
public class BgAmountTranscriberTest
{

    [Test]
    [TestCase(0, "Нула евро и нула цента")]
    [TestCase(0_11, "Нула евро и единадесет цента")]
    // TODO: add more test cases for cents-only values
    [TestCase(1_00, "Едно евро")]
    // TODO: add more test cases for whole-only values
    [TestCase(1_19, "Едно евро и деветнадесет цента")]
    // TODO: add test cases whole-only values
    // TODO: add test cases for single-digit number in whole part and separate tests for same in cent part
    // TODO: add test cases for teen numbers   
    // TODO: add test cases for 2-digit numbers of the form 2x, 3x, 4x, ..., 9x   
    // TODO: add test cases for 3-digit numbers as above   
    // TODO: add test cases for 4-digit numbers as above
    // TODO: add test cases for combinations between different digit count in cents and whole part
    [TestCase(99_99, "Деведтесет и девет евро и деветдесет и девет цента")]
    public void Transcribe_WhenValidAmount_ThenTranscribes(int amountCents, string expected)
    {
        
    }

    [Test]
    public void Transcribe_WhenTranscribingAnyNumberBelow1M_ThenTranscribes()
    {
        // TODO: loop and check every value from 1 to 999999 has a valid string
    }
    
    [Test]
    public void Transcribe_WhenTranscribing1MOrAbove_ThenThrows()
    {
        // TODO: check 1M and a few numbers above throw
    }
    
    [Test]
    // TODO: add test cases
    public void Transcribe_WhenNegativeAmount_ThenThrows()
    {
        
    }
}