using LegoList.Data;
using LegoList.Models;
using LegoList.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegoList.Controllers;

[ApiController]
[Authorize]
[Route("api/lists/{listId}/sets")]
public class SetsController(
    LegoListDbContext db,
    UserService userService,
    RebrickableService rebrickableService,
    ILogger<SetsController> logger) : ControllerBase
{
    private async Task<SetList?> GetOwnedListAsync(int listId)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return null;
        return await db.SetLists.FirstOrDefaultAsync(l => l.Id == listId && l.UserId == user.Id);
    }

    // A cached row with no name usually means an earlier lookup came back empty; look again after this long.
    private static readonly TimeSpan EmptyMetadataRetryAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// Returns cached metadata, or fetches it from Rebrickable. Only definitive answers are stored:
    /// a rate limit or outage must not leave a permanently empty row behind.
    /// </summary>
    private async Task<(SetMetadata? Metadata, bool LookupFailed)> ResolveMetadataAsync(string setNumber, bool force = false)
    {
        var cached = await db.SetMetadatas.FindAsync(setNumber);
        if (!force && cached is not null && (cached.Name is not null || cached.FetchedAt > DateTime.UtcNow - EmptyMetadataRetryAfter))
            return (cached, false);

        var fetched = await rebrickableService.FetchAsync(setNumber);

        if (!fetched.Cacheable)
        {
            // The set number comes from the user; strip line breaks so it can't forge log lines.
            var logSafe = setNumber.Replace("\r", "").Replace("\n", "");
            logger.LogWarning("Rebrickable lookup for {SetNumber} failed; not caching so it is retried next time", logSafe);
            var transient = cached ?? new SetMetadata
            {
                SetNumber = setNumber,
                Name = fetched.Name,
                Theme = fetched.Theme,
                ImageUrl = fetched.ImageUrl,
                PieceCount = fetched.PieceCount,
                FetchedAt = DateTime.UtcNow
            };
            return (transient, true);
        }

        if (cached is not null)
        {
            cached.Name = fetched.Name;
            cached.Theme = fetched.Theme;
            cached.ImageUrl = fetched.ImageUrl;
            cached.PieceCount = fetched.PieceCount;
            cached.FetchedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return (cached, false);
        }

        try
        {
            var metadata = new SetMetadata
            {
                SetNumber = setNumber,
                Name = fetched.Name,
                Theme = fetched.Theme,
                ImageUrl = fetched.ImageUrl,
                PieceCount = fetched.PieceCount,
                FetchedAt = DateTime.UtcNow
            };
            db.SetMetadatas.Add(metadata);
            await db.SaveChangesAsync();
            return (metadata, false);
        }
        catch (DbUpdateException)
        {
            // Another concurrent request cached it first; reload
            db.ChangeTracker.Clear();
            return (await db.SetMetadatas.FindAsync(setNumber), false);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(int listId)
    {
        var list = await GetOwnedListAsync(listId);
        if (list is null) return NotFound();

        var sets = await db.LegoSets
            .Where(s => s.SetListId == listId)
            .Select(s => new
            {
                s.Id,
                s.SetNumber,
                s.Theme,
                s.Name,
                s.Quantity,
                s.SetListId,
                ImageUrl = s.SetMetadata != null ? s.SetMetadata.ImageUrl : null,
                PieceCount = s.SetMetadata != null ? s.SetMetadata.PieceCount : (int?)null
            })
            .ToListAsync();
        return Ok(sets);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int listId, [FromBody] CreateSetRequest request)
    {
        var list = await GetOwnedListAsync(listId);
        if (list is null) return NotFound();

        var (metadata, lookupFailed) = await ResolveMetadataAsync(request.SetNumber);

        var set = new LegoSet
        {
            SetListId = listId,
            SetNumber = request.SetNumber,
            Name = metadata?.Name ?? string.Empty,
            Theme = metadata?.Theme ?? string.Empty,
            Quantity = request.Quantity
        };
        db.LegoSets.Add(set);
        await db.SaveChangesAsync();
        logger.LogInformation("Added set {SetId} ({SetNumber}) to list {ListId}", set.Id, set.SetNumber, listId);

        // Return the same shape as GetAll, not the entity: set.SetList.User... forms a
        // cycle that would make serialization throw (HTTP 500).
        return CreatedAtAction(nameof(GetAll), new { listId }, new
        {
            set.Id,
            set.SetNumber,
            set.Theme,
            set.Name,
            set.Quantity,
            set.SetListId,
            ImageUrl = metadata?.ImageUrl,
            PieceCount = metadata?.PieceCount,
            // True when Rebrickable couldn't be reached (rate limit/outage): the set was added
            // without details, and the refresh action can fill them in later.
            LookupFailed = lookupFailed
        });
    }

    /// <summary>Looks the set up on Rebrickable again (ignoring the cache) and fills in its details.</summary>
    [HttpPost("{id}/refresh")]
    public async Task<IActionResult> Refresh(int listId, int id)
    {
        var list = await GetOwnedListAsync(listId);
        if (list is null) return NotFound();

        var setNumber = await db.LegoSets
            .Where(s => s.Id == id && s.SetListId == listId)
            .Select(s => s.SetNumber)
            .FirstOrDefaultAsync();
        if (setNumber is null) return NotFound();

        var (metadata, lookupFailed) = await ResolveMetadataAsync(setNumber, force: true);
        if (lookupFailed)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                Error = "Couldn't reach Rebrickable right now (it may be rate limiting). Try again in a minute."
            });
        }

        // ResolveMetadataAsync may reset the change tracker, so load the set afterwards.
        var set = await db.LegoSets.FirstAsync(s => s.Id == id && s.SetListId == listId);
        var found = metadata?.Name is not null;
        if (found)
        {
            // Only fill in what Rebrickable returned; don't blank out anything the user typed.
            set.Name = metadata!.Name!;
            if (metadata.Theme is not null) set.Theme = metadata.Theme;
            await db.SaveChangesAsync();
        }

        return Ok(new
        {
            set.Id,
            set.SetNumber,
            set.Theme,
            set.Name,
            set.Quantity,
            set.SetListId,
            ImageUrl = metadata?.ImageUrl,
            PieceCount = metadata?.PieceCount,
            Found = found
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int listId, int id, [FromBody] SetRequest request)
    {
        var list = await GetOwnedListAsync(listId);
        if (list is null) return NotFound();

        var set = await db.LegoSets.FirstOrDefaultAsync(s => s.Id == id && s.SetListId == listId);
        if (set is null) return NotFound();

        set.SetNumber = request.SetNumber;
        set.Theme = request.Theme;
        set.Name = request.Name;
        set.Quantity = request.Quantity;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int listId, int id)
    {
        var list = await GetOwnedListAsync(listId);
        if (list is null) return NotFound();

        var set = await db.LegoSets.FirstOrDefaultAsync(s => s.Id == id && s.SetListId == listId);
        if (set is null) return NotFound();

        db.LegoSets.Remove(set);
        await db.SaveChangesAsync();
        logger.LogInformation("Deleted set {SetId} from list {ListId}", id, listId);
        return NoContent();
    }
}

public record CreateSetRequest(string SetNumber, int Quantity);
public record SetRequest(string SetNumber, string Theme, string Name, int Quantity);
