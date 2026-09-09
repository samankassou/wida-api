namespace Wida.Dal.Entities;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string GoogleSubject { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
