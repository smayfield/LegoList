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
    private const int MaxListNameLength = 255; // set_lists.name is VARCHAR(255)

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

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? format, [FromQuery] string? ids)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        var kind = format?.Trim().ToLowerInvariant();
        if (kind is not ("csv" or "pdf"))
            return BadRequest(new { Error = "format must be 'csv' or 'pdf'." });

        // No ids means every list.
        var wanted = new List<int>();
        foreach (var part in (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var id)) return BadRequest(new { Error = $"'{part}' is not a list id." });
            wanted.Add(id);
        }

        var query = db.SetLists.Where(l => l.UserId == user.Id);
        if (wanted.Count > 0)
            query = query.Where(l => wanted.Contains(l.Id));

        var rows = await query
            .OrderBy(l => l.Name).ThenBy(l => l.Id)
            .Select(l => new
            {
                l.Name,
                Sets = l.Sets
                    .OrderBy(s => s.Theme).ThenBy(s => s.Name).ThenBy(s => s.SetNumber)
                    .Select(s => new
                    {
                        s.SetNumber,
                        s.Name,
                        s.Theme,
                        s.Quantity,
                        PieceCount = s.SetMetadata != null ? s.SetMetadata.PieceCount : (int?)null
                    }).ToList()
            })
            .ToListAsync();

        if (wanted.Count > 0 && rows.Count == 0) return NotFound();

        var lists = rows
            .Select(r => new ExportList(r.Name,
                r.Sets.Select(s => new ExportSet(s.SetNumber, s.Name, s.Theme, s.Quantity, s.PieceCount)).ToList()))
            .ToList();

        var stamp = DateTime.UtcNow;
        var baseName = lists.Count == 1 ? SafeFileName(lists[0].Name) : "legolist-export";
        var fileName = $"{baseName}-{stamp:yyyyMMdd}.{kind}";
        return kind == "csv"
            ? File(ListExportService.ToCsv(lists), "text/csv; charset=utf-8", fileName)
            : File(ListExportService.ToPdf(lists, stamp), "application/pdf", fileName);
    }

    private static string SafeFileName(string name)
    {
        var cleaned = new string(name.Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray()).Trim('-');
        while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");
        return cleaned.Length == 0 ? "legolist" : cleaned;
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        var term = q?.Trim().ToLower();
        if (string.IsNullOrEmpty(term)) return Ok(Array.Empty<object>());

        var sets = await db.LegoSets
            .Where(s => s.SetList.UserId == user.Id
                && (s.Name.ToLower().Contains(term)
                    || s.SetNumber.ToLower().Contains(term)
                    || s.Theme.ToLower().Contains(term)))
            .OrderBy(s => s.SetList.Name).ThenBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.SetNumber,
                s.Theme,
                s.Name,
                s.Quantity,
                s.SetListId,
                ListName = s.SetList.Name,
                ImageUrl = s.SetMetadata != null ? s.SetMetadata.ImageUrl : null,
                PieceCount = s.SetMetadata != null ? s.SetMetadata.PieceCount : (int?)null
            })
            .ToListAsync();
        return Ok(sets);
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
        // Return a plain shape (like GetAll), not the entity: list.User.SetLists points
        // back to the list, and serializing that cycle throws (HTTP 500).
        return CreatedAtAction(nameof(GetById), new { id = list.Id },
            new { list.Id, list.Name, SetCount = 0 });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Rename(int id, [FromBody] CreateListRequest request)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) return BadRequest(new { Error = "A list needs a name." });
        if (name.Length > MaxListNameLength) return BadRequest(new { Error = $"A list name can be at most {MaxListNameLength} characters." });

        var list = await db.SetLists.FirstOrDefaultAsync(l => l.Id == id && l.UserId == user.Id);
        if (list is null) return NotFound();

        list.Name = name;
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Merges list <c>request.FromListId</c> into list <c>id</c>: its sets move over (a set the target
    /// already has gets its quantity added instead of appearing twice), then the source list is deleted.
    /// The surviving list keeps its name unless <c>NewName</c> is given.
    /// </summary>
    [HttpPost("{id}/merge")]
    public async Task<IActionResult> Merge(int id, [FromBody] MergeListsRequest request)
    {
        var user = await userService.GetOrCreateAsync(User);
        if (user is null) return Unauthorized();

        if (request.FromListId == id) return BadRequest(new { Error = "Pick two different lists to merge." });

        var newName = request.NewName?.Trim();
        if (request.NewName is not null)
        {
            if (string.IsNullOrEmpty(newName)) return BadRequest(new { Error = "A list needs a name." });
            if (newName.Length > MaxListNameLength) return BadRequest(new { Error = $"A list name can be at most {MaxListNameLength} characters." });
        }

        var target = await db.SetLists.FirstOrDefaultAsync(l => l.Id == id && l.UserId == user.Id);
        var source = await db.SetLists.FirstOrDefaultAsync(l => l.Id == request.FromListId && l.UserId == user.Id);
        if (target is null || source is null) return NotFound();

        var targetSets = await db.LegoSets.Where(s => s.SetListId == target.Id).ToListAsync();
        var sourceSets = await db.LegoSets.Where(s => s.SetListId == source.Id).ToListAsync();
        foreach (var set in sourceSets)
        {
            var existing = targetSets.FirstOrDefault(t => t.SetNumber == set.SetNumber);
            if (existing is not null)
            {
                existing.Quantity += set.Quantity;
                db.LegoSets.Remove(set);
            }
            else
            {
                set.SetListId = target.Id;
                targetSets.Add(set);
            }
        }
        if (newName is not null) target.Name = newName;
        await db.SaveChangesAsync();

        // The source list is empty now. Reload so the deleted list no longer has any sets attached.
        db.ChangeTracker.Clear();
        var emptied = await db.SetLists.FirstAsync(l => l.Id == request.FromListId);
        db.SetLists.Remove(emptied);
        await db.SaveChangesAsync();

        logger.LogInformation("Merged list {FromId} into {IntoId} for user {UserId}", request.FromListId, id, user.Id);
        var merged = await db.SetLists
            .Where(l => l.Id == id)
            .Select(l => new { l.Id, l.Name, SetCount = l.Sets.Count })
            .FirstAsync();
        return Ok(merged);
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

public record MergeListsRequest(int FromListId, string? NewName);
