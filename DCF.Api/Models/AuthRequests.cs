namespace DCF.Api.Models;

public record UpsertUserRequest(string? DisplayName, string? Email);
public record LogoutRequest(string? RememberToken);
