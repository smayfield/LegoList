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
    public async Task Create_ResponseBody_SerializesWithoutCycle()
    {
        var (controller, db, _, list) = Scaffold();
        db.SetMetadatas.Add(new SetMetadata
        {
            SetNumber = "75192",
            Name = "Millennium Falcon",
            Theme = "Star Wars",
            ImageUrl = "https://example.com/75192.jpg",
            PieceCount = 7541,
            FetchedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await controller.Create(list.Id, new CreateSetRequest("75192", 1));

        // Regression: returning the entity (set.SetList.User...) threw an object-cycle error.
        var created = Assert.IsType<CreatedAtActionResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(created.Value));
        Assert.Equal("Millennium Falcon", doc.RootElement.GetProperty("Name").GetString());
        Assert.Equal(7541, doc.RootElement.GetProperty("PieceCount").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("SetList", out _));
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
    public async Task Create_DoesNotCacheMetadata_WhenRebrickableIsRateLimited()
    {
        var rebrickable = CreateRebrickable(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var (controller, db, _, list) = Scaffold(rebrickable: rebrickable);

        var result = await controller.Create(list.Id, new CreateSetRequest("75192", 1));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(1, db.LegoSets.Count());   // the set is still added
        Assert.Empty(db.SetMetadatas);          // but the failure isn't remembered
    }

    [Fact]
    public async Task Create_RetriesLookup_AfterEarlierFailureWasNotCached()
    {
        var fail = true;
        var rebrickable = CreateRebrickable(_ => fail
            ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : Json("{\"name\":\"Falcon\",\"theme_id\":null,\"set_img_url\":null,\"num_parts\":7541}"));
        var (controller, db, _, list) = Scaffold(rebrickable: rebrickable);

        await controller.Create(list.Id, new CreateSetRequest("75192", 1));
        fail = false;
        await controller.Create(list.Id, new CreateSetRequest("75192", 1));

        var cached = Assert.Single(db.SetMetadatas);
        Assert.Equal("Falcon", cached.Name);
        Assert.Equal(7541, cached.PieceCount);
    }

    [Fact]
    public async Task Create_RefetchesStaleEmptyMetadata_AndFillsItIn()
    {
        var rebrickable = CreateRebrickable(_ => Json(
            "{\"name\":\"Falcon\",\"theme_id\":null,\"set_img_url\":\"https://img.com/f.jpg\",\"num_parts\":7541}"));
        var (controller, db, _, list) = Scaffold(rebrickable: rebrickable);
        db.SetMetadatas.Add(new SetMetadata { SetNumber = "75192", FetchedAt = DateTime.UtcNow.AddHours(-2) });
        await db.SaveChangesAsync();

        await controller.Create(list.Id, new CreateSetRequest("75192", 1));

        var cached = Assert.Single(db.SetMetadatas);
        Assert.Equal("Falcon", cached.Name);
        Assert.Equal("https://img.com/f.jpg", cached.ImageUrl);
        Assert.Equal("Falcon", db.LegoSets.Single().Name);
    }

    [Fact]
    public async Task Create_DoesNotRefetch_RecentEmptyMetadata()
    {
        var calls = 0;
        var rebrickable = CreateRebrickable(_ => { calls++; return Json("{\"name\":\"Falcon\"}"); });
        var (controller, db, _, list) = Scaffold(rebrickable: rebrickable);
        db.SetMetadatas.Add(new SetMetadata { SetNumber = "99999", FetchedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await controller.Create(list.Id, new CreateSetRequest("99999", 1));

        Assert.Equal(0, calls);
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

    [Fact]
    public async Task Create_ReportsLookupFailed_WhenRebrickableIsRateLimited()
    {
        var rebrickable = CreateRebrickable(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var (controller, _, _, list) = Scaffold(rebrickable: rebrickable);

        var result = await controller.Create(list.Id, new CreateSetRequest("75192", 1));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(created.Value));
        Assert.True(doc.RootElement.GetProperty("LookupFailed").GetBoolean());
    }

    [Fact]
    public async Task Create_ReportsLookupSucceeded_WhenRebrickableAnswers()
    {
        var (controller, _, _, list) = Scaffold(); // 404 is a definitive answer

        var result = await controller.Create(list.Id, new CreateSetRequest("99999", 1));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(created.Value));
        Assert.False(doc.RootElement.GetProperty("LookupFailed").GetBoolean());
    }

    // ── Refresh ──────────────────────────────────────────────────────────────

    private static async Task<LegoSet> AddBareSet(LegoListDbContext db, SetList list, string number = "75192")
    {
        var set = new LegoSet { SetListId = list.Id, SetNumber = number, Name = string.Empty, Theme = string.Empty, Quantity = 1 };
        db.LegoSets.Add(set);
        await db.SaveChangesAsync();
        return set;
    }

    [Fact]
    public async Task Refresh_ReturnsNotFound_WhenListNotOwned()
    {
        var (controller, db, _, list) = Scaffold();
        var set = await AddBareSet(db, list);

        var result = await controller.Refresh(999, set.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Refresh_ReturnsNotFound_WhenSetMissing()
    {
        var (controller, _, _, list) = Scaffold();

        var result = await controller.Refresh(list.Id, 12345);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Refresh_FillsInDetails_AndCachesThem()
    {
        var rebrickable = CreateRebrickable(req => req.RequestUri!.ToString().Contains("/themes/")
            ? Json("{\"name\":\"Star Wars\"}")
            : Json("{\"name\":\"Falcon\",\"theme_id\":158,\"set_img_url\":\"https://img.com/f.jpg\",\"num_parts\":7541}"));
        var (controller, db, _, list) = Scaffold(rebrickable: rebrickable);
        var set = await AddBareSet(db, list);

        var result = await controller.Refresh(list.Id, set.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.Equal("Falcon", doc.RootElement.GetProperty("Name").GetString());
        Assert.Equal(7541, doc.RootElement.GetProperty("PieceCount").GetInt32());
        Assert.True(doc.RootElement.GetProperty("Found").GetBoolean());
        var saved = db.LegoSets.Single();
        Assert.Equal("Falcon", saved.Name);
        Assert.Equal("Star Wars", saved.Theme);
        Assert.Equal("Falcon", db.SetMetadatas.Single().Name);
    }

    [Fact]
    public async Task Refresh_IgnoresTheCache_AndUpdatesAnEmptyRow()
    {
        var rebrickable = CreateRebrickable(_ => Json("{\"name\":\"Falcon\",\"theme_id\":null,\"num_parts\":7541}"));
        var (controller, db, _, list) = Scaffold(rebrickable: rebrickable);
        db.SetMetadatas.Add(new SetMetadata { SetNumber = "75192", FetchedAt = DateTime.UtcNow }); // fresh but empty
        var set = await AddBareSet(db, list);

        await controller.Refresh(list.Id, set.Id);

        Assert.Equal(7541, db.SetMetadatas.Single().PieceCount);
        Assert.Equal("Falcon", db.LegoSets.Single().Name);
    }

    [Fact]
    public async Task Refresh_Returns503_WhenRebrickableIsRateLimited()
    {
        var rebrickable = CreateRebrickable(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var (controller, db, _, list) = Scaffold(rebrickable: rebrickable);
        var set = await AddBareSet(db, list);

        var result = await controller.Refresh(list.Id, set.Id);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, status.StatusCode);
        Assert.Empty(db.SetMetadatas);
    }

    [Fact]
    public async Task Refresh_KeepsUserEditedName_WhenRebrickableDoesNotKnowTheSet()
    {
        var (controller, db, _, list) = Scaffold(); // 404
        var set = await AddBareSet(db, list, "99999");
        set.Name = "My custom MOC";
        await db.SaveChangesAsync();

        var result = await controller.Refresh(list.Id, set.Id);

        var ok = Assert.IsType<OkObjectResult>(result);
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value));
        Assert.False(doc.RootElement.GetProperty("Found").GetBoolean());
        Assert.Equal("My custom MOC", db.LegoSets.Single().Name);
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
