using System.ComponentModel.DataAnnotations;

namespace TodoApp.Data;

public class TaskItem
{
    public int Id { get; set; }
    [MaxLength(450)]
    public string UserId { get; set; } = "";
    [MaxLength(120)]
    public string Title { get; set; } = "";
    [MaxLength(2000)]
    public string Description { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public DateTime? FinishDate { get; set; }
    public bool IsCompleted { get; set; }
}
