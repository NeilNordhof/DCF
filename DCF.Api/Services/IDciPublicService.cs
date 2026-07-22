namespace DCF.Api.Services;

public interface IDciPublicService
{
    Task<DciSeasonDto?> GetCurrentSeasonAsync();
}
