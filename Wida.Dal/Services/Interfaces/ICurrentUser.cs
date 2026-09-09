namespace Wida.Dal.Services.Interfaces;

public interface ICurrentUser
{
    Guid? UserId { get; }
}
