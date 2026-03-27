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

        // Use cached metadata if available, otherwise fetch from Rebrickable
        var metadata = await db.SetMetadatas.FindAsync(request.SetNumber);
        if (metadata is null)
        {
            var fetched = await rebrickableService.FetchAsync(request.SetNumber);
            try
            {
                metadata = new SetMetadata
                {
                    SetNumber = request.SetNumber,
                    Name = fetched.Name,
                    Theme = fetched.Theme,
                    ImageUrl = fetched.ImageUrl,
                    PieceCount = fetched.PieceCount,
                    FetchedAt = DateTime.UtcNow
                };
                db.SetMetadatas.Add(metadata);
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Another concurrent request cached it first; reload
                db.ChangeTracker.Clear();
                metadata = await db.SetMetadatas.FindAsync(request.SetNumber);
            }
        }

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

        return CreatedAtAction(nameof(GetAll), new { listId }, set);
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
