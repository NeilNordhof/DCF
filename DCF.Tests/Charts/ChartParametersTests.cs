using DCF.Api.Charts;
using Xunit;

namespace DCF.Tests.Charts;

public class ChartParametersTests
{
    [Fact]
    public void GetGuid_MissingParameter_Throws()
    {
        var parameters = ChartTestHelpers.Params();

        Assert.Throws<ChartParameterException>(() => parameters.GetGuid("seasonId"));
    }

    [Fact]
    public void GetGuid_ParsesStringGuid()
    {
        var id = Guid.NewGuid();
        var parameters = ChartTestHelpers.Params(("seasonId", id.ToString()));

        Assert.Equal(id, parameters.GetGuid("seasonId"));
    }

    [Fact]
    public void GetGuidList_AcceptsScalarAndDeduplicates()
    {
        var id = Guid.NewGuid();

        Assert.Equal([id], ChartTestHelpers.Params(("corpsIds", id.ToString())).GetGuidList("corpsIds", true));
        Assert.Equal(
            [id],
            ChartTestHelpers.Params(("corpsIds", new[] { id.ToString(), id.ToString() })).GetGuidList("corpsIds", true));
    }

    [Fact]
    public void GetGuidList_OptionalMissing_ReturnsEmpty()
    {
        Assert.Empty(ChartTestHelpers.Params().GetGuidList("userIds", required: false));
    }

    [Fact]
    public void GetGuidList_RequiredButEmptyArray_Throws()
    {
        var parameters = ChartTestHelpers.Params(("corpsIds", Array.Empty<string>()));

        Assert.Throws<ChartParameterException>(() => parameters.GetGuidList("corpsIds", required: true));
    }

    [Fact]
    public void GetGuidList_NonGuidEntry_Throws()
    {
        var parameters = ChartTestHelpers.Params(("corpsIds", new[] { "not-a-guid" }));

        Assert.Throws<ChartParameterException>(() => parameters.GetGuidList("corpsIds", required: true));
    }

    [Fact]
    public void GetInt_And_GetBool_ParseStringsAndNatives()
    {
        var parameters = ChartTestHelpers.Params(("limit", 5), ("asText", "7"), ("flag", true), ("flagText", "false"));

        Assert.Equal(5, parameters.GetInt("limit"));
        Assert.Equal(7, parameters.GetInt("asText"));
        Assert.True(parameters.GetBool("flag"));
        Assert.False(parameters.GetBool("flagText"));
        Assert.Null(parameters.GetInt("missing"));
        Assert.Null(parameters.GetBool("missing"));
    }

    [Fact]
    public void ParameterNames_AreCaseInsensitive()
    {
        var id = Guid.NewGuid();
        var parameters = ChartTestHelpers.Params(("SeasonId", id.ToString()));

        Assert.Equal(id, parameters.GetGuid("seasonId"));
    }

    [Fact]
    public void NullValue_IsTreatedAsMissing()
    {
        var parameters = ChartTestHelpers.Params(("seasonId", null));

        Assert.False(parameters.TryGetGuid("seasonId", out _));
    }
}
