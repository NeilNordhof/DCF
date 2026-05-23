using DCF.Data.Entities;

namespace DCF.Api.Services;

// TEMP: placeholder until SeasonStatusService is implemented in Task 4
public class NoOpSeasonStatusService : ISeasonStatusService
{
    public void ScheduleSeason(SeasonEntity season) { }
}
