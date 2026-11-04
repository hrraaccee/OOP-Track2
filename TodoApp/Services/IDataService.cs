using TodoApp.Data;

namespace TodoApp.Services;

public interface IDataService
{
    Task<List<TaskItem>> GetTaskItemsAsync();
    Task<TaskItem?> GetTaskAsync(int id);
    Task<int> SaveAsync(int? id, TaskInput input);
    Task<bool> DeleteAsync(int id);
}
