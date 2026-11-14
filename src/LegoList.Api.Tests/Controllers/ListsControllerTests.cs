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

    // ── Search ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("falcon", 1)]
    [InlineData("75192", 1)]
    [InlineData("STAR WARS", 2)]
    [InlineData("zzz", 0)]
    [InlineData("  ", 0)]
    public async Task Search_MatchesNameNumberOrTheme_OnlyForCurrentUser(string q, int expected)
    {
        var (controller, db) = CreateControllerWithDb("sub-1");
        var u1 = new User { GoogleSub = "sub-1", Email = "a@a.com", CreatedAt = DateTime.UtcNow };
        var u2 = new User { GoogleSub = "sub-2", Email = "b@b.com", CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(u1, u2);
        await db.SaveChangesAsync();
        var mine = new SetList { Name = "Mine", UserId = u1.Id };
        var theirs = new SetList { Name = "Theirs", UserId = u2.Id };
        db.SetLists.AddRange(mine, theirs);
        await db.SaveChangesAsync();
        db.LegoSets.AddRange(
            new LegoSet { SetNumber = "75192", Theme = "Star Wars", Name = "Millennium Falcon", Quantity = 1, SetListId = mine.Id },
            new LegoSet { SetNumber = "75257", Theme = "Star Wars", Name = "Rise Set", Quantity = 1, SetListId = mine.Id },
            new LegoSet { SetNumber = "10001", Theme = "Star Wars", Name = "Not mine", Quantity = 1, SetListId = theirs.Id });
        await db.SaveChangesAsync();

        var result = await controller.Search(q);

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(expected, doc.RootElement.GetArrayLength());
    }

    // ── Rename ───────────────────────────────────────────────────────────────

    private static async Task<(ListsController controller, LegoList.Data.LegoListDbContext db, User user)> ScaffoldUser(string sub = "sub-1")
    {
        var (controller, db) = CreateControllerWithDb(sub);
        var user = new User { GoogleSub = sub, Email = sub + "@example.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (controller, db, user);
    }

    private static async Task<SetList> AddList(LegoList.Data.LegoListDbContext db, User user, string name, params (string number, int qty)[] sets)
    {
        var list = new SetList { Name = name, UserId = user.Id };
        db.SetLists.Add(list);
        await db.SaveChangesAsync();
        foreach (var (number, qty) in sets)
            db.LegoSets.Add(new LegoSet { SetListId = list.Id, SetNumber = number, Name = "Set " + number, Theme = "T", Quantity = qty });
        await db.SaveChangesAsync();
        return list;
    }

    [Fact]
    public async Task Rename_TrimsAndSavesTheName()
    {
        var (controller, db, user) = await ScaffoldUser();
        var list = await AddList(db, user, "Old");

        var result = await controller.Rename(list.Id, new CreateListRequest("  New name  "));

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("New name", db.SetLists.Single().Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_RejectsABlankName(string name)
    {
        var (controller, db, user) = await ScaffoldUser();
        var list = await AddList(db, user, "Old");

        var result = await controller.Rename(list.Id, new CreateListRequest(name));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Old", db.SetLists.Single().Name);
    }

    [Fact]
    public async Task Rename_RejectsATooLongName()
    {
        var (controller, db, user) = await ScaffoldUser();
        var list = await AddList(db, user, "Old");

        var result = await controller.Rename(list.Id, new CreateListRequest(new string('x', 256)));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Rename_ReturnsNotFound_ForSomeoneElsesList()
    {
        var (controller, db, _) = await ScaffoldUser("sub-1");
        var other = new User { GoogleSub = "sub-2", Email = "b@b.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(other);
        await db.SaveChangesAsync();
        var theirs = await AddList(db, other, "Theirs");

        var result = await controller.Rename(theirs.Id, new CreateListRequest("Mine now"));

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal("Theirs", db.SetLists.Single().Name);
    }

    // ── Merge ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Merge_MovesSetsIntoTarget_AndDeletesTheSource()
    {
        var (controller, db, user) = await ScaffoldUser();
        var target = await AddList(db, user, "Target", ("111", 1));
        var source = await AddList(db, user, "Source", ("222", 1), ("333", 2));

        var result = await controller.Merge(target.Id, new MergeListsRequest(source.Id, null));

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("Target", doc.RootElement.GetProperty("Name").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("SetCount").GetInt32());
        Assert.Equal(target.Id, Assert.Single(db.SetLists).Id);
        Assert.Equal(3, db.LegoSets.Count(s => s.SetListId == target.Id));
        Assert.Equal(3, db.LegoSets.Count());
    }

    [Fact]
    public async Task Merge_CombinesQuantities_OfSetsInBothLists()
    {
        var (controller, db, user) = await ScaffoldUser();
        var target = await AddList(db, user, "Target", ("111", 1), ("222", 1));
        var source = await AddList(db, user, "Source", ("111", 4));

        await controller.Merge(target.Id, new MergeListsRequest(source.Id, null));

        Assert.Equal(2, db.LegoSets.Count());
        Assert.Equal(5, db.LegoSets.Single(s => s.SetNumber == "111").Quantity);
    }

    [Fact]
    public async Task Merge_CanGiveTheResultANewName()
    {
        var (controller, db, user) = await ScaffoldUser();
        var target = await AddList(db, user, "Target");
        var source = await AddList(db, user, "Source");

        await controller.Merge(target.Id, new MergeListsRequest(source.Id, "  Everything  "));

        Assert.Equal("Everything", Assert.Single(db.SetLists).Name);
    }

    [Fact]
    public async Task Merge_RejectsABlankNewName()
    {
        var (controller, db, user) = await ScaffoldUser();
        var target = await AddList(db, user, "Target");
        var source = await AddList(db, user, "Source");

        var result = await controller.Merge(target.Id, new MergeListsRequest(source.Id, "  "));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(2, db.SetLists.Count());
    }

    [Fact]
    public async Task Merge_RejectsMergingAListIntoItself()
    {
        var (controller, db, user) = await ScaffoldUser();
        var list = await AddList(db, user, "Only", ("111", 1));

        var result = await controller.Merge(list.Id, new MergeListsRequest(list.Id, null));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(db.LegoSets);
    }

    [Fact]
    public async Task Merge_ReturnsNotFound_WhenEitherListBelongsToSomeoneElse()
    {
        var (controller, db, user) = await ScaffoldUser("sub-1");
        var other = new User { GoogleSub = "sub-2", Email = "b@b.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(other);
        await db.SaveChangesAsync();
        var mine = await AddList(db, user, "Mine", ("111", 1));
        var theirs = await AddList(db, other, "Theirs", ("222", 1));

        Assert.IsType<NotFoundResult>(await controller.Merge(mine.Id, new MergeListsRequest(theirs.Id, null)));
        Assert.IsType<NotFoundResult>(await controller.Merge(theirs.Id, new MergeListsRequest(mine.Id, null)));
        Assert.Equal(2, db.SetLists.Count());
        Assert.Equal(2, db.LegoSets.Count());
    }

    // ── Export ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_Csv_ReturnsAllOfTheUsersLists()
    {
        var (controller, db, user) = await ScaffoldUser();
        await AddList(db, user, "One", ("111", 1));
        await AddList(db, user, "Two", ("222", 2));
        var other = new User { GoogleSub = "sub-2", Email = "b@b.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(other);
        await db.SaveChangesAsync();
        await AddList(db, other, "Theirs", ("999", 1));

        var result = await controller.Export("csv", null);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.StartsWith("text/csv", file.ContentType);
        Assert.StartsWith("legolist-export-", file.FileDownloadName);
        Assert.EndsWith(".csv", file.FileDownloadName);
        var text = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("111", text);
        Assert.Contains("222", text);
        Assert.DoesNotContain("999", text);
    }

    [Fact]
    public async Task Export_OnlyIncludesTheRequestedLists()
    {
        var (controller, db, user) = await ScaffoldUser();
        var one = await AddList(db, user, "One", ("111", 1));
        await AddList(db, user, "Two", ("222", 2));

        var result = await controller.Export("csv", one.Id.ToString());

        var file = Assert.IsType<FileContentResult>(result);
        var text = System.Text.Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains("111", text);
        Assert.DoesNotContain("222", text);
        Assert.Equal($"One-{DateTime.UtcNow:yyyyMMdd}.csv", file.FileDownloadName);
    }

    [Fact]
    public async Task Export_Pdf_ReturnsAPdf()
    {
        var (controller, db, user) = await ScaffoldUser();
        await AddList(db, user, "One", ("111", 1));

        var result = await controller.Export("PDF", null);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 5));
    }

    [Fact]
    public async Task Export_IncludesPieceCountsFromMetadata()
    {
        var (controller, db, user) = await ScaffoldUser();
        await AddList(db, user, "One", ("75192", 2));
        db.SetMetadatas.Add(new SetMetadata { SetNumber = "75192", PieceCount = 7541, FetchedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var file = Assert.IsType<FileContentResult>(await controller.Export("csv", null));

        Assert.Contains(",2,7541,15082", System.Text.Encoding.UTF8.GetString(file.FileContents));
    }

    [Theory]
    [InlineData("docx", null)]
    [InlineData(null, null)]
    [InlineData("csv", "abc")]
    public async Task Export_RejectsBadRequests(string? format, string? ids)
    {
        var (controller, _, _) = await ScaffoldUser();

        Assert.IsType<BadRequestObjectResult>(await controller.Export(format, ids));
    }

    [Fact]
    public async Task Export_ReturnsNotFound_WhenNoRequestedListIsTheirs()
    {
        var (controller, db, user) = await ScaffoldUser();
        await AddList(db, user, "One");

        Assert.IsType<NotFoundResult>(await controller.Export("csv", "9999"));
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

    [Fact]
    public async Task Create_ResponseBody_SerializesWithoutCycle()
    {
        var (controller, _) = CreateControllerWithDb();

        var result = await controller.Create(new CreateListRequest("Wishlist"));

        // Regression: returning the entity threw "A possible object cycle was detected".
        var created = Assert.IsType<CreatedAtActionResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(created.Value));
        Assert.Equal("Wishlist", doc.RootElement.GetProperty("Name").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("SetCount").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("User", out _));
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
