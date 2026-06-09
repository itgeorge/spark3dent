using Orders;

namespace Orders.Tests;

public class PinHasherTest
{
    [Test]
    public void Verify_GivenCorrectPin_ReturnsTrue()
    {
        var hasher = new PinHasher("test-pepper");
        var hash = hasher.Hash("123456", iterations: 10_000);

        var verified = hasher.Verify("123456", hash);

        Assert.That(verified, Is.True);
    }

    [Test]
    public void Verify_GivenWrongPin_ReturnsFalse()
    {
        var hasher = new PinHasher("test-pepper");
        var hash = hasher.Hash("123456", iterations: 10_000);

        var verified = hasher.Verify("654321", hash);

        Assert.That(verified, Is.False);
    }

    [Test]
    public void Hash_GivenCustomSecret_VerifiesSuccessfully()
    {
        var hasher = new PinHasher("test-pepper");
        var hash = hasher.Hash("custom-secret! 2026", iterations: 10_000);

        Assert.That(hasher.Verify("custom-secret! 2026", hash), Is.True);
    }

    [TestCase("12345")]
    [TestCase("")]
    [TestCase("      ")]
    public void Hash_GivenInvalidSecret_Throws(string secret)
    {
        var hasher = new PinHasher("test-pepper");

        var ex = Assert.Throws<InvalidOperationException>(() => hasher.Hash(secret, iterations: 10_000));

        Assert.That(ex!.Message, Is.EqualTo("Invalid credential secret."));
    }

    [Test]
    public void Verify_GivenExistingSixDigitHash_RemainsCompatible()
    {
        var hasher = new PinHasher("test-pepper");
        var hash = hasher.Hash("123456", iterations: 10_000);

        Assert.That(hasher.Verify("123456", hash), Is.True);
    }
}
