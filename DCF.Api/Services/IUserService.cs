namespace DCF.Api.Services;

public interface IUserService
{
    Task<UserProfile?> GetAsync(string sub);
    Task<UserProfile> UpsertAsync(string sub, string email, string name, string? displayName = null);
}
