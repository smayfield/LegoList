using LegoList.Data;
using LegoList.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LegoList.Controllers;

[ApiController]
[Authorize(Policy = AdminAccess.PolicyName)]
[Route("api/admin")]
public class AdminController(LegoListDbContext db, ILogger<AdminController> logger) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);
        var monthAgo = now.AddDays(-30);

        var users = await db.Users.CountAsync();
        var usersLast7Days = await db.Users.CountAsync(u => u.CreatedAt >= weekAgo);
        var usersLast30Days = await db.Users.CountAsync(u => u.CreatedAt >= monthAgo);
        var usersWithSets = await db.LegoSets.Select(s => s.SetList.UserId).Distinct().CountAsync();
        var lists = await db.SetLists.CountAsync();
        var setEntries = await db.LegoSets.CountAsync();
        var totalSets = await db.LegoSets.SumAsync(s => s.Quantity);
        var builtSets = await db.LegoSets.Where(s => s.Built).SumAsync(s => s.Quantity);
        var distinctSets = await db.LegoSets.Select(s => s.SetNumber).Distinct().CountAsync();
        var cachedMetadata = await db.SetMetadatas.CountAsync();

        var themeRows = await db.LegoSets
            .Where(s => s.Theme != "")
            .GroupBy(s => s.Theme)
            .Select(g => new { Name = g.Key, Count = g.Sum(s => s.Quantity) })
            .OrderByDescending(t => t.Count).ThenBy(t => t.Name)
            .Take(10)
            .ToListAsync();
        var topThemes = themeRows.Select(t => new NamedCount(t.Name, t.Count)).ToList();

        // "Most collected" counts how many different users own a set, not how many copies exist.
        var setRows = await db.LegoSets
            .GroupBy(s => new { s.SetNumber, s.Name })
            .Select(g => new
            {
                g.Key.SetNumber,
                g.Key.Name,
                Owners = g.Select(s => s.SetList.UserId).Distinct().Count(),
                Copies = g.Sum(s => s.Quantity)
            })
            .OrderByDescending(t => t.Owners).ThenByDescending(t => t.Copies).ThenBy(t => t.SetNumber)
            .Take(10)
            .ToListAsync();
        var topSets = setRows.Select(t => new TopSet(t.SetNumber, t.Name, t.Owners, t.Copies)).ToList();

        return Ok(new AdminStats(
            users, usersLast7Days, usersLast30Days, usersWithSets, lists, setEntries, totalSets, builtSets,
            distinctSets, cachedMetadata,
            users == 0 ? 0 : Math.Round((double)totalSets / users, 1),
            topThemes, topSets));
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users()
    {
        var users = await db.Users
            .OrderBy(u => u.Email)
            .Select(u => new AdminUserRow(
                u.Id, u.Email, u.CreatedAt,
                u.SetLists.Count,
                u.SetLists.SelectMany(l => l.Sets).Count(),
                u.SetLists.SelectMany(l => l.Sets).Sum(s => (int?)s.Quantity) ?? 0))
            .ToListAsync();
        return Ok(users);
    }

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await db.Users
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.CreatedAt,
                Lists = u.SetLists.OrderBy(l => l.Name).Select(l => new
                {
                    l.Id,
                    l.Name,
                    Sets = l.Sets.OrderBy(s => s.Theme).ThenBy(s => s.SetNumber).Select(s => new
                    {
                        s.Id,
                        s.SetNumber,
                        s.Theme,
                        s.Name,
                        s.Quantity,
                        s.Built,
                        s.DisplayLocation,
                        s.PurchasePrice,
                        s.Notes,
                        PieceCount = s.SetMetadata != null ? s.SetMetadata.PieceCount : (int?)null
                    }).ToList()
                }).ToList()
            })
            .FirstOrDefaultAsync();

        return user is null ? NotFound() : Ok(user);
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await db.Users
            .Include(u => u.SetLists).ThenInclude(l => l.Sets)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user is null) return NotFound();

        if (user.GoogleSub == User.FindFirst(ClaimTypes.NameIdentifier)?.Value)
            return BadRequest(new { Error = "You can't delete your own account from the admin section." });

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        logger.LogWarning("Admin {Admin} deleted user {UserId} ({Email})", User.FindFirst(ClaimTypes.Email)?.Value, id, user.Email);
        return NoContent();
    }

    [HttpDelete("lists/{id:int}")]
    public async Task<IActionResult> DeleteList(int id)
    {
        var list = await db.SetLists.Include(l => l.Sets).FirstOrDefaultAsync(l => l.Id == id);
        if (list is null) return NotFound();

        db.SetLists.Remove(list);
        await db.SaveChangesAsync();
        logger.LogWarning("Admin {Admin} deleted list {ListId} of user {UserId}", User.FindFirst(ClaimTypes.Email)?.Value, id, list.UserId);
        return NoContent();
    }

    [HttpDelete("sets/{id:int}")]
    public async Task<IActionResult> DeleteSet(int id)
    {
        var set = await db.LegoSets.FirstOrDefaultAsync(s => s.Id == id);
        if (set is null) return NotFound();

        db.LegoSets.Remove(set);
        await db.SaveChangesAsync();
        logger.LogWarning("Admin {Admin} deleted set {SetId}", User.FindFirst(ClaimTypes.Email)?.Value, id);
        return NoContent();
    }
}

public record NamedCount(string Name, int Count);

public record TopSet(string SetNumber, string Name, int Owners, int Copies);

public record AdminStats(
    int Users, int UsersLast7Days, int UsersLast30Days, int UsersWithSets,
    int Lists, int SetEntries, int TotalSets, int BuiltSets, int DistinctSets, int CachedMetadata,
    double AverageSetsPerUser, List<NamedCount> TopThemes, List<TopSet> TopSets);

public record AdminUserRow(int Id, string Email, DateTime CreatedAt, int ListCount, int SetEntries, int TotalSets);
