namespace DCF.Api.Services;

public interface IRememberMeTokenService
{
    Task<string> IssueAsync(Guid userId);
    Task<string?> ValidateAsync(string rawToken);
    Task ExtendIfOwnedByAsync(string rawToken, Guid userId);
    Task RevokeAsync(string rawToken);
}
