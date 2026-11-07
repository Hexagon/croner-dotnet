using System;
using System.Linq;

namespace Croner.Tests;

public class ParserTests
{
    [Fact]
    public void ParsesBasicPattern()
    {
        var cron = Cron.Parse("*/5 * * * *");
        Assert.Equal("*/5 * * * *", cron.Pattern.Pattern);
        Assert.Equal("*/5 * * * *", cron.ToString());
        Assert.Equal(new[] { 0 }, cron.Pattern.Seconds.Values());
        Assert.Contains(5, cron.Pattern.Minutes.Values());
    }

    [Fact]
    public void SixFieldsIncludeSeconds()
    {
        var cron = Cron.Parse("* */5 * * * *");
        Assert.Equal(60, cron.Pattern.Seconds.Values().Count);
        Assert.Equal(new[] { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55 }, cron.Pattern.Minutes.Values());
    }

    [Fact]
    public void ShortSteppedPattern()
    {
        var cron = Cron.Parse("5-59/5 * * * *");
        Assert.Equal(new[] { 0 }, cron.Pattern.Seconds.Values());
        Assert.Equal(5, cron.Pattern.Minutes.Values()[0]);
        Assert.DoesNotContain(0, cron.Pattern.Minutes.Values());
    }

    [Theory]
    [InlineData("*/15 1 1,15 1 1-5")]
    [InlineData("  */15  1 1,15 1    1-5    ")]
    [InlineData("  */15  01 01,15 01    01-05    ")]
    public void ParsesFields(string pattern)
    {
        var p = Cron.Parse(pattern).Pattern;
        Assert.Contains(0, p.Minutes.Values());
        Assert.Equal(new[] { 1 }, p.Hours.Values());
        Assert.Equal(new[] { 1, 15 }, p.Days.Values());
        Assert.Equal(new[] { 1 }, p.Months.Values());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, p.DaysOfWeek.Values());
    }

    [Theory]
    [InlineData("@yearly", "0 0 1 1 *")]
    [InlineData("@annually", "0 0 1 1 *")]
    [InlineData("@monthly", "0 0 1 * *")]
    [InlineData("@weekly", "0 0 * * SUN")]
    [InlineData("@daily", "0 0 * * *")]
    [InlineData("@midnight", "0 0 * * *")]
    [InlineData("@hourly", "0 * * * *")]
    [InlineData("@YeArLy", "0 0 1 1 *")]
    [InlineData("@HOURLY", "0 * * * *")]
    public void Nicknames(string nickname, string equivalent)
    {
        Assert.Equal(Cron.Parse(equivalent), Cron.Parse(nickname));
    }

    [Fact]
    public void NicknamesWithRequiredSeconds()
    {
        Assert.Equal("0 0 0 1 1 *", CronParser.HandleNicknames("@yearly", true, false));
        Assert.Equal("0 0 * * * *", CronParser.HandleNicknames("@hourly", true, false));
        Assert.Equal("0 0 0 1 1 * *", CronParser.HandleNicknames("@yearly", true, true));
        var cron = Cron.Parse("@daily", new CronParserOptions { Seconds = SecondsMode.Required });
        Assert.Equal(Cron.Parse("0 0 0 * * *"), cron);
    }

    [Fact]
    public void RebootIsRejected()
    {
        var ex = Assert.Throws<CronException>(() => Cron.Parse("@reboot"));
        Assert.Equal(CronErrorKind.InvalidPattern, ex.Kind);
    }

    [Fact]
    public void MonthNicknameRange()
    {
        Assert.Equal(new[] { 2, 3 }, Cron.Parse("0 0 * FEB-MAR *").Pattern.Months.Values());
    }

    [Fact]
    public void WeekdayRangeSatSun()
    {
        Assert.Equal(new[] { 0, 6 }, Cron.Parse("0 0 * * SAT-SUN").Pattern.DaysOfWeek.Values());
    }

    [Fact]
    public void SevenIsSunday()
    {
        Assert.Equal(new[] { 0 }, Cron.Parse("* * * * 7").Pattern.DaysOfWeek.Values());
    }

    [Fact]
    public void SecondsDisallowed()
    {
        var parser = new CronParser(new CronParserOptions { Seconds = SecondsMode.Disallowed });
        Assert.Equal(CronErrorKind.InvalidPattern, Assert.Throws<CronException>(() => parser.Parse("* * * * * *")).Kind);
        var cron = parser.Parse("*/10 * * * *");
        Assert.Equal("*/10 * * * *", cron.ToString());
        Assert.Equal(new[] { 0 }, cron.Pattern.Seconds.Values());
    }

    [Fact]
    public void SecondsRequired()
    {
        var parser = new CronParser(new CronParserOptions { Seconds = SecondsMode.Required });
        Assert.Equal(CronErrorKind.InvalidPattern, Assert.Throws<CronException>(() => parser.Parse("*/10 * * * *")).Kind);
        Assert.Equal(60, parser.Parse("* * * * * *").Pattern.Seconds.Values().Count);
    }

    [Fact]
    public void AlternativeWeekdays()
    {
        var parser = new CronParser(new CronParserOptions { AlternativeWeekdays = true });
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, parser.Parse("* * * * MON-FRI").Pattern.DaysOfWeek.Values());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, parser.Parse("* * * * 2-6").Pattern.DaysOfWeek.Values());
        Assert.Equal(new[] { 0 }, parser.Parse("* * * * 1").Pattern.DaysOfWeek.Values());
        Assert.Equal(CronErrorKind.ComponentError, Assert.Throws<CronException>(() => parser.Parse("* * * * 0")).Kind);
    }

    [Fact]
    public void AlternativeWeekdaysWildcardStep()
    {
        var strict = new CronParser(new CronParserOptions { AlternativeWeekdays = true });
        var sloppy = new CronParser(new CronParserOptions { AlternativeWeekdays = true, SloppyRanges = true });
        Assert.Equal(new[] { 0, 2, 4, 6 }, strict.Parse("* * * * */2").Pattern.DaysOfWeek.Values());
        Assert.Equal(new[] { 0, 2, 4, 6 }, sloppy.Parse("* * * * /2").Pattern.DaysOfWeek.Values());
    }

    [Theory]
    [InlineData("* * ? * *")]
    [InlineData("* * * * ?")]
    public void QuestionMarkAllowedInDayFields(string pattern) => Cron.Parse(pattern);

    [Theory]
    [InlineData("? * * * *")]
    [InlineData("* ? * * *")]
    [InlineData("* * * ? *")]
    public void QuestionMarkDisallowedElsewhere(string pattern)
    {
        Assert.Equal(CronErrorKind.IllegalCharacters, Assert.Throws<CronException>(() => Cron.Parse(pattern)).Kind);
    }

    [Fact]
    public void QuestionMarkIsAliasForWildcard()
    {
        Assert.Equal(Cron.Parse("0 0 1 * *"), Cron.Parse("0 0 1 * ?"));
    }

    [Theory]
    [InlineData("0 0 L * *", "0 0 l * *")]
    [InlineData("0 0 * * 5L", "0 0 * * 5l")]
    [InlineData("0 0 15W * *", "0 0 15w * *")]
    [InlineData("0 0 LW * *", "0 0 lw * *")]
    [InlineData("0 0 LW * *", "0 0 Lw * *")]
    [InlineData("0 0 * * MON#2", "0 0 * * mon#2")]
    [InlineData("0 0 * JAN-MAR *", "0 0 * jan-mar *")]
    [InlineData("0 0 * * MON-FRI", "0 0 * * Mon-Fri")]
    [InlineData("0 0 1 DEC *", "0 0 1 dec *")]
    public void CaseInsensitive(string a, string b) => Assert.Equal(Cron.Parse(a), Cron.Parse(b));

    [Fact]
    public void YearSupport()
    {
        var parser = new CronParser();
        parser.Parse("0 0 0 1 1 * 2025");
        parser.Parse("0 0 0 1 1 *");
        parser.Parse("0 0 1 1 *");
        Assert.Equal(new[] { 2025 }, parser.Parse("0 0 0 1 1 * 2025").Pattern.Years.Values());
    }

    [Fact]
    public void YearRequired()
    {
        var parser = new CronParser(new CronParserOptions { Seconds = SecondsMode.Required, Year = YearMode.Required });
        parser.Parse("0 0 0 1 1 * 2025");
        Assert.Throws<CronException>(() => parser.Parse("0 0 0 1 1 *"));

        var optionalSeconds = new CronParser(new CronParserOptions { Year = YearMode.Required });
        Assert.Equal(CronErrorKind.InvalidPattern, Assert.Throws<CronException>(() => optionalSeconds.Parse("* * * * * *")).Kind);
    }

    [Theory]
    [InlineData("* * *")]
    [InlineData("invalid")]
    [InlineData("123")]
    [InlineData("0 0 * * * * * *")]
    [InlineData("* * * *")]
    [InlineData("* 60 * * * *")]
    [InlineData("-1 59 * * * *")]
    [InlineData("1- 59 * * * *")]
    [InlineData("0 0 0 5L * *")]
    [InlineData("0 0 0 5#L * *")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0 0 +1 * *")]
    [InlineData("* * * * 1#6")]
    [InlineData("* * * * 1#0")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("0 0 32 * *")]
    [InlineData("0 0 * 13 *")]
    [InlineData("0 24 * * *")]
    [InlineData("0 0 0 * * * 5001")]
    public void InvalidExpressions(string pattern)
    {
        Assert.Throws<CronException>(() => Cron.Parse(pattern));
        Assert.False(Cron.TryParse(pattern, out var cron));
        Assert.Null(cron);
    }

    [Theory]
    [InlineData("* * * * *")]
    [InlineData("0 0 * * *")]
    [InlineData("*/10 * * * *")]
    [InlineData("0 0 1 1 *")]
    [InlineData("0 12 * * MON")]
    [InlineData("0 0   * * 1")]
    [InlineData("0 0 1 1,7 * ")]
    [InlineData("00 00 01 * SUN  ")]
    [InlineData("0 0 1-7 * SUN")]
    [InlineData("5-10/2 * * * *")]
    [InlineData("0 0-23/2 * * *")]
    [InlineData("0 12 15-21 * 1-FRI")]
    [InlineData("0 0 29 2 *")]
    [InlineData("0 0 31 * *")]
    [InlineData("*/15 9-17 * * MON-FRI")]
    [InlineData("0 12 * JAN-JUN *")]
    [InlineData("0 0 1,15,L * SUN#L")]
    [InlineData("0 0 2,1 1-6/2 *")]
    [InlineData("0 0 5,L * 5L")]
    [InlineData("0 0 5,L * 7#2")]
    [InlineData("*/15 0-4,8-12 * JAN-MAR,DEC MON-FRI")]
    [InlineData("0 0 LW * *")]
    [InlineData("0 12 1 * +MON")]
    public void ValidExpressions(string pattern)
    {
        Cron.Parse(pattern);
        Assert.True(Cron.TryParse(pattern, out var cron));
        Assert.NotNull(cron);
    }

    [Theory]
    [InlineData("0/10 * * * *")]
    [InlineData("30/10 * * * *")]
    [InlineData("* 5/15 * * *")]
    [InlineData("* * 15/5 * *")]
    [InlineData("* * * 6/2 *")]
    [InlineData("* * * * 1/2")]
    [InlineData("/10 * * * *")]
    [InlineData("* /5 * * *")]
    public void StrictStepSyntax(string pattern)
    {
        Assert.Equal(CronErrorKind.ComponentError, Assert.Throws<CronException>(() => Cron.Parse(pattern)).Kind);
    }

    [Fact]
    public void SloppyRanges()
    {
        var parser = new CronParser(new CronParserOptions { SloppyRanges = true });
        Assert.Equal(new[] { 0, 10, 20, 30, 40, 50 }, parser.Parse("0/10 * * * *").Pattern.Minutes.Values());
        Assert.Equal(new[] { 30, 40, 50 }, parser.Parse("30/10 * * * *").Pattern.Minutes.Values());
        Assert.Equal(new[] { 0, 10, 20, 30, 40, 50 }, parser.Parse("/10 * * * *").Pattern.Minutes.Values());
    }

    [Fact]
    public void ModifiersReported()
    {
        Assert.False(Cron.Parse("0 9-11 * * 1-5").Pattern.DaysOfWeek.HasModifiers);
        Assert.True(Cron.Parse("0 0 * * 5L").Pattern.DaysOfWeek.HasModifiers);
        Assert.True(Cron.Parse("0 0 L * *").Pattern.Days.HasModifiers);
        Assert.True(Cron.Parse("0 0 15W * *").Pattern.Days.HasModifiers);
        Assert.Equal(new[] { 9, 10, 11 }, Cron.Parse("0 9-11 * * 1-5").Pattern.Hours.Values());
    }

    [Fact]
    public void PlusModifierFlags()
    {
        var p = Cron.Parse("0 12 1 * +MON").Pattern;
        Assert.True(p.MatchesDomAndDow);
        Assert.False(Cron.Parse("0 12 1 * MON").Pattern.MatchesDomAndDow);
        Assert.True(Cron.Parse("0 12 1 * MON", new CronParserOptions { DomAndDow = true }).Pattern.MatchesDomAndDow);
    }

    [Fact]
    public void EqualityIgnoresSpelling()
    {
        Assert.Equal(Cron.Parse("0 0 * * MON-FRI"), Cron.Parse("0 0 * * 1-5"));
        Assert.Equal(Cron.Parse("0 0 * * MON-FRI").GetHashCode(), Cron.Parse("0 0 * * 1-5").GetHashCode());
        Assert.NotEqual(Cron.Parse("0 0 * * MON"), Cron.Parse("0 0 * * TUE"));
        Assert.Equal(Cron.Parse("0 0 * * MON"), Cron.Parse("0 0 * * MON", new CronParserOptions { AlternativeWeekdays = true }));
        Assert.Equal(Cron.Parse("*/5 * * * *"), Cron.Parse("*/5 * * * *", new CronParserOptions { SloppyRanges = true }));
    }

    [Theory]
    [InlineData("0 0 * MON *")]
    [InlineData("0 0 * * JAN")]
    [InlineData("0 0 MON * *")]
    [InlineData("JAN 0 * * *")]
    public void NamesOnlyInTheirOwnField(string pattern)
    {
        Assert.Equal(CronErrorKind.IllegalCharacters, Assert.Throws<CronException>(() => Cron.Parse(pattern)).Kind);
    }

    [Fact]
    public void TryParseNull()
    {
        Assert.False(Cron.TryParse(null, out Cron? cron));
        Assert.Null(cron);
        Assert.True(Cron.TryParse("0 0 * * *", out cron));
        Assert.Equal("0 0 * * *", cron.ToString());
    }

    [Fact]
    public void JobTypes()
    {
        Assert.Equal(JobType.FixedTime, Cron.Parse("0 30 2 * * *").JobType);
        Assert.Equal(JobType.FixedTime, Cron.Parse("30 2 * * *").JobType);
        Assert.Equal(JobType.IntervalWildcard, Cron.Parse("*/5 * * * *").JobType);
        Assert.Equal(JobType.IntervalWildcard, Cron.Parse("0 0,12 * * *").JobType);
        Assert.Equal(JobType.IntervalWildcard, Cron.Parse("0 * * * *").JobType);
    }
}
