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
}
