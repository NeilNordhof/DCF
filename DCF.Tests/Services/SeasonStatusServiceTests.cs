using DCF.Api.Services;
using DCF.Data.Entities;
using DCF.Data.Models;
using Xunit;

namespace DCF.Tests.Services;

public class SeasonStatusServiceTests
{
    [Fact]
    public void ApplyStatusTransitions_UpcomingWithStartDateToday_SetsToActive()
    {
        var today = new DateOnly(2025, 7, 15);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Upcoming,
            StartDate = new DateOnly(2025, 7, 15),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_UpcomingWithStartDateInPast_SetsToActive()
    {
        var today = new DateOnly(2025, 7, 20);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Upcoming,
            StartDate = new DateOnly(2025, 7, 10),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_UpcomingWithStartDateInFuture_NoChange()
    {
        var today = new DateOnly(2025, 7, 1);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Upcoming,
            StartDate = new DateOnly(2025, 7, 10),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Upcoming, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_ActiveWithEndDateBeforeToday_SetsToCompleted()
    {
        var today = new DateOnly(2025, 8, 20);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active,
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Completed, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_ActiveWithEndDateEqualToday_NoChange()
    {
        var today = new DateOnly(2025, 8, 15);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active,
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_ActiveWithEndDateInFuture_NoChange()
    {
        var today = new DateOnly(2025, 7, 15);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active,
            StartDate = new DateOnly(2025, 7, 1),
            EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Active, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_Completed_NeverChanges()
    {
        var today = new DateOnly(2025, 6, 1);
        var season = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2024, Status = SeasonStatus.Completed,
            StartDate = new DateOnly(2024, 7, 1),
            EndDate = new DateOnly(2024, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([season], today);

        Assert.Equal(SeasonStatus.Completed, season.Status);
    }

    [Fact]
    public void ApplyStatusTransitions_MultipleSeasons_TransitionsEachIndependently()
    {
        var today = new DateOnly(2025, 8, 20);
        var upcoming = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2026, Status = SeasonStatus.Upcoming,
            StartDate = new DateOnly(2025, 8, 18), EndDate = new DateOnly(2026, 8, 15)
        };
        var active = new SeasonEntity
        {
            Id = Guid.NewGuid(), Year = 2025, Status = SeasonStatus.Active,
            StartDate = new DateOnly(2025, 7, 1), EndDate = new DateOnly(2025, 8, 15)
        };

        SeasonStatusService.ApplyStatusTransitions([upcoming, active], today);

        Assert.Equal(SeasonStatus.Active, upcoming.Status);
        Assert.Equal(SeasonStatus.Completed, active.Status);
    }
}
