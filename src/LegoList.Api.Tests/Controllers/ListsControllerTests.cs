using LegoList.Controllers;
using LegoList.Models;
using LegoList.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace LegoList.Api.Tests.Controllers;

public class ListsControllerTests
{
    private static ListsController CreateController(string sub = "sub-1")
    {
        var db = TestHelpers.CreateDb();
        return new ListsController(db, new UserService(db), NullLogger<ListsController>.Instance)
            .WithUser(TestHelpers.CreatePrincipal(sub));
    }

    private static (ListsController controller, LegoList.Data.LegoListDbContext db) CreateControllerWithDb(string sub = "sub-1")
    {
        var db = TestHelpers.CreateDb();
        var controller = new ListsController(db, new UserService(db), NullLogger<ListsController>.Instance)
            .WithUser(TestHelpers.CreatePrincipal(sub));
        return (controller, db);
    }

    // ── GetAll ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoListsExist()
    {
        var controller = CreateController();

        var result = await controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value);
        Assert.Empty(items.Cast<object>());
    }

    [Fact]
    public async Task GetAll_ReturnsOnlyCurrentUserLists()
    {
        var (controller, db) = CreateControllerWithDb("sub-1");
        var u1 = new User { GoogleSub = "sub-1", Email = "a@a.com", CreatedAt = DateTime.UtcNow };
        var u2 = new User { GoogleSub = "sub-2", Email = "b@b.com", CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(u1, u2);
        await db.SaveChangesAsync();
        db.SetLists.AddRange(
            new SetList { Name = "My List", UserId = u1.Id },
            new SetList { Name = "Their List", UserId = u2.Id });
        await db.SaveChangesAsync();

        var result = await controller.GetAll();

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("My List", doc.RootElement[0].GetProperty("Name").GetString());
    }

    // ── GetById ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenListDoesNotExist()
    {
        var controller = CreateController();

        var result = await controller.GetById(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenListBelongsToOtherUser()
    {
        var (controller, db) = CreateControllerWithDb("sub-1");
        var other = new User { GoogleSub = "sub-2", Email = "b@b.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(other);
        await db.SaveChangesAsync();
        var list = new SetList { Name = "Theirs", UserId = other.Id };
        db.SetLists.Add(list);
        await db.SaveChangesAsync();

        var result = await controller.GetById(list.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetById_ReturnsOk_WithNameAndSets()
    {
        var (controller, db) = CreateControllerWithDb("sub-1");
        var user = new User { GoogleSub = "sub-1", Email = "a@a.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var list = new SetList { Name = "Mine", UserId = user.Id };
        db.SetLists.Add(list);
        await db.SaveChangesAsync();
        db.LegoSets.Add(new LegoSet { SetNumber = "75192", Name = "Falcon", Theme = "SW", Quantity = 1, SetListId = list.Id });
        await db.SaveChangesAsync();

        var result = await controller.GetById(list.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("Mine", doc.RootElement.GetProperty("Name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("Sets").GetArrayLength());
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsCreated_AndPersistsList()
    {
        var (controller, db) = CreateControllerWithDb();

        var result = await controller.Create(new CreateListRequest("Wishlist"));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(1, db.SetLists.Count());
        Assert.Equal("Wishlist", db.SetLists.Single().Name);
    }

    // ── Rename ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rename_ReturnsNotFound_WhenListNotOwned()
    {
        var controller = CreateController();

        var result = await controller.Rename(999, new CreateListRequest("New Name"));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Rename_ReturnsNoContent_AndUpdatesName()
    {
        var (controller, db) = CreateControllerWithDb("sub-1");
        var user = new User { GoogleSub = "sub-1", Email = "a@a.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var list = new SetList { Name = "Old Name", UserId = user.Id };
        db.SetLists.Add(list);
        await db.SaveChangesAsync();

        var result = await controller.Rename(list.Id, new CreateListRequest("New Name"));

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("New Name", db.SetLists.Find(list.Id)!.Name);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenListNotOwned()
    {
        var controller = CreateController();

        var result = await controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_AndRemovesList()
    {
        var (controller, db) = CreateControllerWithDb("sub-1");
        var user = new User { GoogleSub = "sub-1", Email = "a@a.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var list = new SetList { Name = "To Delete", UserId = user.Id };
        db.SetLists.Add(list);
        await db.SaveChangesAsync();

        var result = await controller.Delete(list.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, db.SetLists.Count());
    }
}
