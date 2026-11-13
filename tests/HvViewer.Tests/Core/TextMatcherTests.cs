using HvViewer.Core.Model;
using HvViewer.Core.Query;
using Xunit;

namespace HvViewer.Tests.Core;

public class TextMatcherTests
{
    [Fact] public void Plain_IsCaseInsensitiveByDefault() =>
        Assert.True(new TextMatcher("disk 3", new()).IsMatch("…for Disk 3 was retried."));

    [Fact] public void MatchCase_Respected() =>
        Assert.False(new TextMatcher("disk 3", new(MatchCase: true)).IsMatch("Disk 3"));

    [Fact] public void WholeWord_DoesNotMatchInsideWord() =>
        Assert.False(new TextMatcher("Disk 3", new(WholeWord: true)).IsMatch("Disk 31"));

    [Fact] public void WholeWord_MatchesStandaloneWord() =>
        Assert.True(new TextMatcher("Disk 3", new(WholeWord: true)).IsMatch("The Disk 3, again"));

    [Fact] public void IgnoreAccents_MatchesFrench() =>
        Assert.True(new TextMatcher("echec", new(IgnoreAccents: true)).IsMatch("Échec de l'ouverture de session"));

    [Fact] public void AccentSensitive_ByDefault() =>
        Assert.False(new TextMatcher("echec", new()).IsMatch("Échec"));

    [Fact] public void Matches_ReturnsSpans() =>
        Assert.Equal([(12, 6)], new TextMatcher("Disk 3", new()).Matches("IO error on Disk 3."));


    [Fact] public void Matches_IgnoreAccents_LengthCoversOriginalText() =>
        Assert.Equal([(3, 5)], new TextMatcher("echec", new(IgnoreAccents: true)).Matches("Un Échec de plus"));

    [Fact] public void Regex_Matches() =>
        Assert.True(new TextMatcher(@"Disk \d", new(Regex: true)).IsMatch("error on Disk 3"));

    [Fact] public void Regex_Catastrophic_TimesOutAsNoMatch()
    {
        var m = new TextMatcher("(a+)+$", new(Regex: true));
        Assert.False(m.IsMatch(new string('a', 5000) + "!"));
        Assert.Equal(1, m.TimeoutCount);
    }

    [Fact] public void Null_IsNoMatch() => Assert.False(new TextMatcher("x", new()).IsMatch(null));
}
