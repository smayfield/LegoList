using LegoList.Data;
using LegoList.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LegoList.Services;

public class UserService(LegoListDbContext db)
{
    public async Task<User?> GetOrCreateAsync(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sub)) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSub == sub);
        if (user is null)
        {
            user = new User
            {
                GoogleSub = sub,
                Email = principal.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
        return user;
    }
}
