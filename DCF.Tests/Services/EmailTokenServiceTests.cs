using DCF.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace DCF.Tests.Services;

public class EmailTokenServiceTests
{
    private static EmailTokenService Create(string secret = "test-secret-32-chars-minimum-len!")
    {
        var opts = Options.Create(new EmailOptions { UnsubscribeSecret = secret });

        return new EmailTokenService(opts);
    }

    [Fact]
    public void GenerateToken_ValidateToken_RoundTrip()
    {
        var svc = Create();
        var userId = Guid.NewGuid();
        var token = svc.GenerateToken(userId);
        var result = svc.ValidateToken(token);

        Assert.Equal(userId, result);
    }

    [Fact]
    public void ValidateToken_TamperedHmac_ReturnsNull()
    {
        var svc = Create();
        var token = svc.GenerateToken(Guid.NewGuid());
        var id = token[..token.IndexOf(':')];
        var tampered = $"{id}:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        Assert.Null(svc.ValidateToken(tampered));
    }

    [Fact]
    public void ValidateToken_WrongSecret_ReturnsNull()
    {
        var svc1 = Create("secret-one-32-chars-minimum-lena");
        var svc2 = Create("secret-two-32-chars-minimum-lenb");
        var token = svc1.GenerateToken(Guid.NewGuid());

        Assert.Null(svc2.ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_MalformedToken_ReturnsNull()
    {
        var svc = Create();

        Assert.Null(svc.ValidateToken("not-a-valid-token"));
        Assert.Null(svc.ValidateToken(""));
        Assert.Null(svc.ValidateToken("::::"));
    }
}
