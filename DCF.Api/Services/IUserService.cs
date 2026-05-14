namespace DCF.Api.Services;

public interface IUserService
{
    Task<UserProfile> UpsertAsync(string sub, string email, string name);
}
