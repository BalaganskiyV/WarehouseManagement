using System.ComponentModel.DataAnnotations;
namespace Application.DTOs;
public sealed class WarehouseEventInput : IValidatableObject
{
    [Required, RegularExpression("^(Arrival|Departure|Inventory)$")]
    public string EventType { get; set; } = "";
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(0, int.MaxValue)] public int Quantity { get; set; }
    [Required, RegularExpression(@"^[A-Z]-[1-9][0-9]{0,2}$")]
    public string WarehouseZone { get; set; } = "";
    public DateTimeOffset? EventDate { get; set; }
    [StringLength(2000)] public string Description { get; set; } = "";
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (EventType != "Inventory" && Quantity == 0)
            yield return new ValidationResult("Quantity must be positive for Arrival and Departure.", [nameof(Quantity)]);
        if (EventDate.HasValue && (EventDate.Value.Year < 2000 || EventDate.Value > DateTimeOffset.UtcNow.AddMinutes(1)))
            yield return new ValidationResult("EventDate must be between 2000-01-01 and the current UTC time.", [nameof(EventDate)]);
    }
}
