namespace DCF.Api.Models;

public record UnsubscribeRequest(string Token);

public record UpdateNotificationPreferencesRequest(bool EmailNotificationsEnabled);
