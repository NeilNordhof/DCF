using DCF.Data.Entities;

namespace DCF.Api.Services;

public interface ISeasonStatusService
{
    void ScheduleSeason(SeasonEntity season);
}
