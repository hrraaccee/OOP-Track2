using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace TodoApp.Data;

public class TaskInput : IValidatableObject
{
    [Required(ErrorMessage = "Введите заголовок задачи.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "Заголовок должен содержать от 3 до 120 символов.")]
    public string Title { get; set; } = "";
    [Required(ErrorMessage = "Введите описание задачи.")]
    [StringLength(2000, ErrorMessage = "Описание должно содержать не более 2000 символов.")]
    public string Description { get; set; } = "";
    public string? FinishDate { get; set; }
    public bool IsCompleted { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(FinishDate) &&
            !DateTime.TryParseExact(FinishDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            yield return new ValidationResult("Укажите корректную дату окончания.", [nameof(FinishDate)]);
    }
}
