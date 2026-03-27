using LegoList.Data;
using LegoList.Models;
using LegoList.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LegoList.Controllers;

[ApiController]
[Authorize]
[Route("api/lists")]
public class ListsController(LegoListDbContext db, UserService userService, ILogger<ListsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        var lists = await db.SetLists
            .Where(l => l.UserId == user.Id)
            .Select(l => new { l.Id, l.Name, SetCount = l.Sets.Count })
            .ToListAsync();
        return Ok(lists);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        var list = await db.SetLists
            .Where(l => l.Id == id && l.UserId == user.Id)
            .Select(l => new
            {
                l.Id,
                l.Name,
                Sets = l.Sets.Select(s => new
                {
                    s.Id,
                    s.SetNumber,
                    s.Theme,
                    s.Name,
                    s.Quantity,
                    s.SetListId,
                    ImageUrl = s.SetMetadata != null ? s.SetMetadata.ImageUrl : null,
                    PieceCount = s.SetMetadata != null ? s.SetMetadata.PieceCount : (int?)null
                }).ToList()
            })
            .FirstOrDefaultAsync();

        if (list is null) return NotFound();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateListRequest request)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        var list = new SetList { Name = request.Name, UserId = user.Id };
        db.SetLists.Add(list);
        await db.SaveChangesAsync();
        logger.LogInformation("Created list {Id} '{Name}' for user {UserId}", list.Id, list.Name, user.Id);
        return CreatedAtAction(nameof(GetById), new { id = list.Id }, list);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Rename(int id, [FromBody] CreateListRequest request)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        var list = await db.SetLists.FirstOrDefaultAsync(l => l.Id == id && l.UserId == user.Id);
        if (list is null) return NotFound();

        list.Name = request.Name;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        var list = await db.SetLists.FirstOrDefaultAsync(l => l.Id == id && l.UserId == user.Id);
        if (list is null) return NotFound();

        db.SetLists.Remove(list);
        await db.SaveChangesAsync();
        logger.LogInformation("Deleted list {Id} for user {UserId}", id, user.Id);
        return NoContent();
    }
}

public record CreateListRequest(string Name);
