namespace DCF.Api.Services;

public interface IPickTimerService
{
    void SchedulePickExpiration(Guid leagueId, int pickNumber, DateTimeOffset deadline);
    void CancelPickTimer(Guid leagueId);
}
