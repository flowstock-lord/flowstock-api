using FlowStock.Infrastructure.Identity;

namespace FlowStock.UnitTests.Identity;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_does_not_contain_the_plain_password()
    {
        var hash = _hasher.Hash("Correct horse battery staple");

        Assert.NotEmpty(hash);
        Assert.DoesNotContain("Correct horse battery staple", hash);
    }

    [Fact]
    public void Verify_accepts_the_original_password()
    {
        var hash = _hasher.Hash("Admin123!");

        Assert.True(_hasher.Verify(hash, "Admin123!"));
    }

    [Fact]
    public void Verify_rejects_a_wrong_password()
    {
        var hash = _hasher.Hash("Admin123!");

        Assert.False(_hasher.Verify(hash, "admin123!"));
        Assert.False(_hasher.Verify(hash, "something else"));
    }

    [Fact]
    public void Hashing_the_same_password_twice_produces_different_hashes()
    {
        Assert.NotEqual(_hasher.Hash("Admin123!"), _hasher.Hash("Admin123!"));
    }
}
