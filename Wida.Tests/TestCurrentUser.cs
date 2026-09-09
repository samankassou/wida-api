using Wida.Dal.Services.Interfaces;

namespace Wida.Tests;

internal sealed record TestCurrentUser(Guid? UserId) : ICurrentUser
{
    public static TestCurrentUser Default { get; } = new(Guid.Parse("dc06d3ef-b15d-46d7-9114-276b33323340"));
}
