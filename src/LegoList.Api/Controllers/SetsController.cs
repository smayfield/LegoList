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
    public async Task<IActionResult> Create(int listId, [FromBody] SetRequest request)
    {
        var list = await GetOwnedListAsync(listId);
        if (list is null) return NotFound();

        var set = new LegoSet
        {
            SetListId = listId,
            SetNumber = request.SetNumber,
            Theme = request.Theme,
            Name = request.Name,
            Quantity = request.Quantity
        };
        db.LegoSets.Add(set);
        await db.SaveChangesAsync();
        logger.LogInformation("Added set {SetId} to list {ListId}", set.Id, listId);

        // Fetch and cache Rebrickable metadata the first time this set number is added
        if (!await db.SetMetadatas.AnyAsync(m => m.SetNumber == request.SetNumber))
        {
            var (imageUrl, pieceCount) = await rebrickableService.FetchAsync(request.SetNumber);
            if (imageUrl is not null || pieceCount is not null)
            {
                db.SetMetadatas.Add(new SetMetadata
                {
                    SetNumber = request.SetNumber,
                    ImageUrl = imageUrl,
                    PieceCount = pieceCount,
                    FetchedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
        }

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

public record SetRequest(string SetNumber, string Theme, string Name, int Quantity);
