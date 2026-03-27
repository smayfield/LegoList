using LegoList.Services;
using System.Security.Claims;
using Xunit;

namespace LegoList.Api.Tests.Services;

public class UserServiceTests
{
    [Fact]
    public async Task GetOrCreateAsync_ReturnsNull_WhenNoNameIdentifierClaim()
    {
        using var db = TestHelpers.CreateDb();
        var service = new UserService(db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var result = await service.GetOrCreateAsync(principal);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesNewUser_WithCorrectFields()
    {
        using var db = TestHelpers.CreateDb();
        var service = new UserService(db);

        var result = await service.GetOrCreateAsync(TestHelpers.CreatePrincipal("sub-123", "user@example.com"));

        Assert.NotNull(result);
        Assert.Equal("sub-123", result.GoogleSub);
        Assert.Equal("user@example.com", result.Email);
        Assert.Equal(1, db.Users.Count());
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsExistingUser_WhenCalledTwice()
    {
        using var db = TestHelpers.CreateDb();
        var service = new UserService(db);
        var principal = TestHelpers.CreatePrincipal("sub-123");

        var first = await service.GetOrCreateAsync(principal);
        var second = await service.GetOrCreateAsync(principal);

        Assert.Equal(first!.Id, second!.Id);
        Assert.Equal(1, db.Users.Count());
    }

    [Fact]
    public async Task GetOrCreateAsync_UsesEmptyEmail_WhenEmailClaimMissing()
    {
        using var db = TestHelpers.CreateDb();
        var service = new UserService(db);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "sub-only")], "Test"));

        var result = await service.GetOrCreateAsync(principal);

        Assert.NotNull(result);
        Assert.Equal(string.Empty, result.Email);
    }

    [Fact]
    public async Task GetOrCreateAsync_IsolatesUsers_ByGoogleSub()
    {
        using var db = TestHelpers.CreateDb();
        var service = new UserService(db);

        var user1 = await service.GetOrCreateAsync(TestHelpers.CreatePrincipal("sub-A", "a@test.com"));
        var user2 = await service.GetOrCreateAsync(TestHelpers.CreatePrincipal("sub-B", "b@test.com"));

        Assert.NotEqual(user1!.Id, user2!.Id);
        Assert.Equal(2, db.Users.Count());
    }
}
