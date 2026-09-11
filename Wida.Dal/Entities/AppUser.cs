namespace Wida.Dal.Entities;

public class AppUser
{
    public Wida.Dal.Enums.UserRole Role { get; set; } = Wida.Dal.Enums.UserRole.User;

    public int AnalysisPagesGranted { get; set; } = 4;
    public int AnalysisPagesUsed { get; set; }
    public DateTime? CreditRequestedAt { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public string GoogleSubject { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
