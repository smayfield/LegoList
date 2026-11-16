using LegoList.Controllers;
using LegoList.Models;
using LegoList.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace LegoList.Api.Tests.Controllers;

public class AdminControllerTests
{
    private static ClaimsPrincipal Principal(string? email, string? verified = "true")
    {
        var claims = new List<Claim>();
        if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));
        if (verified is not null) claims.Add(new Claim("email_verified", verified));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Theory]
    [InlineData("guppyboy@gmail.com", "true", true)]
    [InlineData("GuppyBoy@Gmail.com", "true", true)]
    [InlineData("guppyboy@gmail.com", "false", false)]
    [InlineData("guppyboy@gmail.com", null, false)]
    [InlineData("someone@else.com", "true", false)]
    [InlineData(null, "true", false)]
    public void IsAdmin_RequiresListedVerifiedEmail(string? email, string? verified, bool expected)
    {
        Assert.Equal(expected, AdminAccess.IsAdmin(Principal(email, verified), ["guppyboy@gmail.com"]));
    }

    [Fact]
    public void IsAdmin_NobodyWhenNoAdminsConfigured()
    {
        Assert.False(AdminAccess.IsAdmin(Principal("guppyboy@gmail.com"), []));
    }

    private static async Task<(AdminController controller, LegoList.Data.LegoListDbContext db)> Seed()
    {
        var db = TestHelpers.CreateDb();
        var admin = new User { GoogleSub = "admin", Email = "guppyboy@gmail.com", CreatedAt = DateTime.UtcNow };
        var alice = new User { GoogleSub = "alice", Email = "alice@x.com", CreatedAt = DateTime.UtcNow.AddDays(-40) };
        var bob = new User { GoogleSub = "bob", Email = "bob@x.com", CreatedAt = DateTime.UtcNow.AddDays(-3) };
        db.Users.AddRange(admin, alice, bob);
        await db.SaveChangesAsync();
        var aList = new SetList { Name = "Alice's", UserId = alice.Id };
        var bList = new SetList { Name = "Bob's", UserId = bob.Id };
        db.SetLists.AddRange(aList, bList);
        await db.SaveChangesAsync();
        db.LegoSets.AddRange(
            new LegoSet { SetNumber = "75192", Theme = "Star Wars", Name = "Falcon", Quantity = 2, Built = true, SetListId = aList.Id },
            new LegoSet { SetNumber = "10001", Theme = "City", Name = "Bus", Quantity = 1, SetListId = aList.Id },
            new LegoSet { SetNumber = "75192", Theme = "Star Wars", Name = "Falcon", Quantity = 1, SetListId = bList.Id });
        await db.SaveChangesAsync();
        var controller = new AdminController(db, NullLogger<AdminController>.Instance)
            .WithUser(TestHelpers.CreatePrincipal("admin", "guppyboy@gmail.com"));
        return (controller, db);
    }

    [Fact]
    public async Task Stats_CountsEverything()
    {
        var (controller, _) = await Seed();

        var stats = Assert.IsType<AdminStats>(Assert.IsType<OkObjectResult>(await controller.Stats()).Value);

        Assert.Equal(3, stats.Users);
        Assert.Equal(2, stats.UsersLast7Days);
        Assert.Equal(2, stats.UsersLast30Days);
        Assert.Equal(2, stats.UsersWithSets);
        Assert.Equal(2, stats.Lists);
        Assert.Equal(3, stats.SetEntries);
        Assert.Equal(4, stats.TotalSets);
        Assert.Equal(2, stats.BuiltSets);
        Assert.Equal(2, stats.DistinctSets);
        Assert.Equal("Star Wars", stats.TopThemes[0].Name);
        Assert.Equal(3, stats.TopThemes[0].Count);
        Assert.Equal(new TopSet("75192", "Falcon", 2, 3), stats.TopSets[0]);
    }

    [Fact]
    public async Task Stats_EmptyDatabase_IsAllZero()
    {
        var db = TestHelpers.CreateDb();
        var controller = new AdminController(db, NullLogger<AdminController>.Instance).WithUser(TestHelpers.CreatePrincipal());

        var stats = Assert.IsType<AdminStats>(Assert.IsType<OkObjectResult>(await controller.Stats()).Value);

        Assert.Equal(0, stats.Users);
        Assert.Equal(0, stats.TotalSets);
        Assert.Equal(0, stats.AverageSetsPerUser);
        Assert.Empty(stats.TopSets);
    }

    [Fact]
    public async Task Users_ListsAllUsersWithTotals()
    {
        var (controller, _) = await Seed();

        var rows = Assert.IsType<List<AdminUserRow>>(Assert.IsType<OkObjectResult>(await controller.Users()).Value);

        Assert.Equal(3, rows.Count);
        var alice = rows.Single(r => r.Email == "alice@x.com");
        Assert.Equal(1, alice.ListCount);
        Assert.Equal(2, alice.SetEntries);
        Assert.Equal(3, alice.TotalSets);
        Assert.Equal(0, rows.Single(r => r.Email == "guppyboy@gmail.com").TotalSets);
    }

    [Fact]
    public async Task GetUser_UnknownId_IsNotFound()
    {
        var (controller, _) = await Seed();
        Assert.IsType<NotFoundResult>(await controller.GetUser(9999));
    }

    [Fact]
    public async Task DeleteUser_RemovesUserListsAndSets()
    {
        var (controller, db) = await Seed();
        var alice = await db.Users.SingleAsync(u => u.Email == "alice@x.com");

        Assert.IsType<NoContentResult>(await controller.DeleteUser(alice.Id));

        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(1, await db.SetLists.CountAsync());
        Assert.Equal(1, await db.LegoSets.CountAsync());
    }

    [Fact]
    public async Task DeleteUser_OwnAccount_IsRefused()
    {
        var (controller, db) = await Seed();
        var admin = await db.Users.SingleAsync(u => u.GoogleSub == "admin");

        Assert.IsType<BadRequestObjectResult>(await controller.DeleteUser(admin.Id));
        Assert.Equal(3, await db.Users.CountAsync());
    }

    [Fact]
    public async Task DeleteList_And_DeleteSet_RemoveOnlyThatItem()
    {
        var (controller, db) = await Seed();
        var set = await db.LegoSets.FirstAsync(s => s.SetNumber == "10001");
        Assert.IsType<NoContentResult>(await controller.DeleteSet(set.Id));
        Assert.Equal(2, await db.LegoSets.CountAsync());

        var bobList = await db.SetLists.SingleAsync(l => l.Name == "Bob's");
        Assert.IsType<NoContentResult>(await controller.DeleteList(bobList.Id));
        Assert.Equal(1, await db.SetLists.CountAsync());
        Assert.Equal(1, await db.LegoSets.CountAsync());

        Assert.IsType<NotFoundResult>(await controller.DeleteList(9999));
        Assert.IsType<NotFoundResult>(await controller.DeleteSet(9999));
    }
}
