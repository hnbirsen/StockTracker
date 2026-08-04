using FluentAssertions;
using StockTracker.Shared.Scraping.Http;

namespace StockTracker.Shared.Scraping.Tests;

public class BrowserProfilesTests
{
    [Fact]
    public void All_ChromiumProfiles_CarryConsistentSecChUaHeaders()
    {
        var chromiumProfiles = BrowserProfiles.All.Where(p => p.UserAgent.Contains("Chrome/"));

        chromiumProfiles.Should().NotBeEmpty();
        chromiumProfiles.Should().OnlyContain(p => p.SecChUa != null && p.SecChUaPlatform != null && p.SecChUaMobile != null);
    }

    [Fact]
    public void All_FirefoxAndSafariProfiles_DoNotCarrySecChUaHeaders()
    {
        // Yalnızca Chromium tabanlı tarayıcılar sec-ch-ua* Client Hint header'larını gönderir — Firefox/Safari
        // UA'sıyla bu header'ları göndermek motor/tarayıcı tutarsızlığı yüzünden bir bot sinyali olurdu.
        var nonChromiumProfiles = BrowserProfiles.All.Where(p => p.UserAgent.Contains("Firefox/") || p.UserAgent.Contains("Version/"));

        nonChromiumProfiles.Should().NotBeEmpty();
        nonChromiumProfiles.Should().OnlyContain(p => p.SecChUa == null && p.SecChUaPlatform == null && p.SecChUaMobile == null);
    }

    [Fact]
    public void Random_AlwaysReturnsAKnownProfile()
    {
        for (var i = 0; i < 50; i++)
        {
            BrowserProfiles.All.Should().Contain(BrowserProfiles.Random());
        }
    }
}
