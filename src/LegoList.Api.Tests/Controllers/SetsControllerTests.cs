using LegoList.Controllers;
using LegoList.Data;
using LegoList.Models;
using LegoList.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LegoList.Api.Tests.Controllers;

public class SetsControllerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RebrickableService CreateRebrickable(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new StubHttpMessageHandler(respond))
        {
            BaseAddress = new Uri("https://rebrickable.com/")
        };
        return new RebrickableService(http, NullLogger<RebrickableService>.Instance);
    }

    private static RebrickableService NotFoundRebrickable()
        => CreateRebrickable(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Creates a fresh DB, seeds a user and a list, and wires up a controller.</summary>
    private static (SetsController controller, LegoListDbContext db, User user, SetList list) Scaffold(
        string sub = "sub-1",
        RebrickableService? rebrickable = null)
    {
        var db = TestHelpers.CreateDb();
        var user = new User { GoogleSub = sub, Email = "test@example.com", CreatedAt = DateTime.UtcNow };
        db.Users.Add(user);
        db.SaveChanges();
        var list = new SetList { Name = "Test List", UserId = user.Id };
        db.SetLists.Add(list);
        db.SaveChanges();

        var controller = new SetsController(
                db,
                new UserService(db),
                rebrickable ?? NotFoundRebrickable(),
                NullLogger<SetsController>.Instance)
            .WithUser(TestHelpers.CreatePrincipal(sub));

        return (controller, db, user, list);
    }

    // ── GetAll ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsNotFound_WhenListNotOwned()
    {
        var (controller, _, _, _) = Scaffold();

        var result = await controller.GetAll(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetAll_ReturnsOk_WithSetsInList()
    {
        var (controller, db, _, list) = Scaffold();
        db.LegoSets.Add(new LegoSet { SetNumber = "75192", Name = "Falcon", Theme = "SW", Quantity = 2, SetListId = list.Id });
        await db.SaveChangesAsync();

        var result = await controller.GetAll(list.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(1, doc.RootElement.GetArrayLength());
        Assert.Equal("75192", doc.RootElement[0].GetProperty("SetNumber").GetString());
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyArray_WhenNoSets()
    {
        var (controller, _, _, list) = Scaffold();

        var result = await controller.GetAll(list.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    // ── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ReturnsNotFound_WhenListNotOwned()
    {
        var (controller, _, _, _) = Scaffold();

        var result = await controller.Create(999, new CreateSetRequest("75192", 1));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Create_UsesCachedMetadata_WhenAlreadyStored()
    {
        var (controller, db, _, list) = Scaffold();
        db.SetMetadatas.Add(new SetMetadata
        {
            SetNumber = "75192",
            Name = "Millennium Falcon",
            Theme = "Star Wars",
            FetchedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Create(list.Id, new CreateSetRequest("75192", 1));

        Assert.IsType<CreatedAtActionResult>(result);
        var legoSet = db.LegoSets.Single();
        Assert.Equal("Millennium Falcon", legoSet.Name);
        Assert.Equal("Star Wars", legoSet.Theme);
    }

    [Fact]
    public async Task Create_FetchesAndCachesMetadata_WhenNotCached()
    {
        var rebrickable = CreateRebrickable(_ => Json(
            "{\"name\":\"Falcon\",\"theme_id\":null,\"set_img_url\":\"https://img.com/f.jpg\",\"num_parts\":7541}"));
        var (controller, db, _, list) = Scaffold(rebrickable: rebrickable);

        var result = await controller.Create(list.Id, new CreateSetRequest("75192", 1));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(1, db.SetMetadatas.Count());
        var cached = db.SetMetadatas.Single();
        Assert.Equal("75192", cached.SetNumber);
        Assert.Equal("Falcon", cached.Name);
        Assert.Equal("https://img.com/f.jpg", cached.ImageUrl);
        Assert.Equal(7541, cached.PieceCount);
    }

    [Fact]
    public async Task Create_UsesEmptyStrings_WhenMetadataFetchFails()
    {
        var (controller, db, _, list) = Scaffold(); // rebrickable returns 404

        var result = await controller.Create(list.Id, new CreateSetRequest("99999", 1));

        Assert.IsType<CreatedAtActionResult>(result);
        var legoSet = db.LegoSets.Single();
        Assert.Equal(string.Empty, legoSet.Name);
        Assert.Equal(string.Empty, legoSet.Theme);
    }

    [Fact]
    public async Task Create_ReturnsCreated_WithCorrectSetNumber()
    {
        var (controller, db, _, list) = Scaffold();

        var result = await controller.Create(list.Id, new CreateSetRequest("42083", 3));

        Assert.IsType<CreatedAtActionResult>(result);
        var legoSet = db.LegoSets.Single();
        Assert.Equal("42083", legoSet.SetNumber);
        Assert.Equal(3, legoSet.Quantity);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ReturnsNotFound_WhenListNotOwned()
    {
        var (controller, _, _, _) = Scaffold();

        var result = await controller.Update(999, 1, new SetRequest("75192", "SW", "Falcon", 1));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenSetNotFound()
    {
        var (controller, _, _, list) = Scaffold();

        var result = await controller.Update(list.Id, 999, new SetRequest("75192", "SW", "Falcon", 1));

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Update_ReturnsNoContent_AndUpdatesAllFields()
    {
        var (controller, db, _, list) = Scaffold();
        var set = new LegoSet { SetNumber = "75192", Name = "Old", Theme = "OldTheme", Quantity = 1, SetListId = list.Id };
        db.LegoSets.Add(set);
        await db.SaveChangesAsync();

        var result = await controller.Update(list.Id, set.Id, new SetRequest("75192-1", "Star Wars", "Millennium Falcon", 3));

        Assert.IsType<NoContentResult>(result);
        var updated = db.LegoSets.Find(set.Id)!;
        Assert.Equal("75192-1", updated.SetNumber);
        Assert.Equal("Star Wars", updated.Theme);
        Assert.Equal("Millennium Falcon", updated.Name);
        Assert.Equal(3, updated.Quantity);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenSetBelongsToOtherList()
    {
        var (controller, db, user, list) = Scaffold("sub-1");
        var otherList = new SetList { Name = "Other", UserId = user.Id };
        db.SetLists.Add(otherList);
        await db.SaveChangesAsync();
        var set = new LegoSet { SetNumber = "75192", Name = "Falcon", Theme = "SW", Quantity = 1, SetListId = otherList.Id };
        db.LegoSets.Add(set);
        await db.SaveChangesAsync();

        var result = await controller.Update(list.Id, set.Id, new SetRequest("75192", "SW", "Falcon", 1));

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenListNotOwned()
    {
        var (controller, _, _, _) = Scaffold();

        var result = await controller.Delete(999, 1);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenSetNotFound()
    {
        var (controller, _, _, list) = Scaffold();

        var result = await controller.Delete(list.Id, 999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_ReturnsNoContent_AndRemovesSet()
    {
        var (controller, db, _, list) = Scaffold();
        var set = new LegoSet { SetNumber = "75192", Name = "Falcon", Theme = "SW", Quantity = 1, SetListId = list.Id };
        db.LegoSets.Add(set);
        await db.SaveChangesAsync();

        var result = await controller.Delete(list.Id, set.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(0, db.LegoSets.Count());
    }
}
