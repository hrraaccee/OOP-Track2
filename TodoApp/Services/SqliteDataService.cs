using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using TodoApp.Data;

namespace TodoApp.Services;

public sealed class SqliteDataService(IDbContextFactory<ApplicationDbContext> factory, AuthenticationStateProvider authentication) : IDataService
{
    private async Task<string> UserIdAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true) throw new UnauthorizedAccessException();
        return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
    }

    public async Task<List<TaskItem>> GetTaskItemsAsync()
    {
        var userId = await UserIdAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.TaskItems.AsNoTracking().Where(item => item.UserId == userId)
            .OrderBy(item => item.IsCompleted).ThenByDescending(item => item.CreatedDate).ThenByDescending(item => item.Id).ToListAsync();
    }

    public async Task<TaskItem?> GetTaskAsync(int id)
    {
        var userId = await UserIdAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.TaskItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId);
    }

    public async Task<int> SaveAsync(int? id, TaskInput input)
    {
        var userId = await UserIdAsync();
        input.Title = input.Title?.Trim() ?? "";
        input.Description = input.Description?.Trim() ?? "";
        Validator.ValidateObject(input, new ValidationContext(input), true);
        await using var db = await factory.CreateDbContextAsync();
        TaskItem item;
        if (id.HasValue)
            item = await db.TaskItems.SingleOrDefaultAsync(task => task.Id == id && task.UserId == userId)
                ?? throw new KeyNotFoundException("Задача не найдена.");
        else
        {
            item = new TaskItem { UserId = userId, CreatedDate = DateTime.UtcNow };
            db.TaskItems.Add(item);
        }
        item.Title = input.Title;
        item.Description = input.Description;
        item.FinishDate = string.IsNullOrWhiteSpace(input.FinishDate) ? null
            : DateTime.ParseExact(input.FinishDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        item.IsCompleted = input.IsCompleted;
        await db.SaveChangesAsync();
        return item.Id;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var userId = await UserIdAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.TaskItems.Where(item => item.Id == id && item.UserId == userId).ExecuteDeleteAsync() == 1;
    }
}
