using Croner.Describing;

namespace Croner.Tests;

public class DescribeTests
{
    private static string Describe(string pattern, bool domAndDow = false) =>
        Cron.Parse(pattern, new CronParserOptions { DomAndDow = domAndDow }).Describe();

    [Theory]
    [InlineData("* * * * *", "Every minute.")]
    [InlineData("*/15 * * * *", "At every 15 minutes.")]
    [InlineData("0 * * * *", "At minute 0 past every hour.")]
    [InlineData("0 14 * * *", "At 14:00.")]
    [InlineData("2,4,6 * * * *", "At minute 2, 4, and 6 past every hour.")]
    [InlineData("0 0-6 * * *", "At minute 0, of hour 0-6.")]
    [InlineData("0 */2 * * *", "At minute 0, of every 2 hours.")]
    [InlineData("* 0 * * *", "Every minute past hour 0.")]
    [InlineData("* 0,12 * * *", "Every minute past hour 0 and 12.")]
    [InlineData("* 0-5 * * *", "Every minute past hour 0-5.")]
    [InlineData("*/10 * * * * *", "At every 10 seconds.")]
    [InlineData("30 0 14 * * *", "At 14:00:30.")]
    [InlineData("10-20 0 14 * * *", "At 14:00, at second 10-20.")]
    [InlineData("* * 0 * * *", "Every second past hour 0.")]
    [InlineData("* * 0,12 * * *", "Every second past hour 0 and 12.")]
    [InlineData("0 0 0 1 1 * 2025", "At 00:00, on day 1, in January, in year 2025.")]
    [InlineData("0 0 0 1 1 * 2025-2030", "At 00:00, on day 1, in January, in year 2025-2030.")]
    [InlineData("0 12 * * MON", "At 12:00, on Monday.")]
    [InlineData("0 12 * * 1-5", "At 12:00, on Monday, Tuesday, Wednesday, Thursday, and Friday.")]
    [InlineData("0 12 15 * *", "At 12:00, on day 15.")]
    [InlineData("0 12 L * *", "At 12:00, on the last day of the month.")]
    [InlineData("0 12 1,15 * *", "At 12:00, on day 1 and 15.")]
    [InlineData("* * * JAN *", "Every minute, in January.")]
    [InlineData("* * * 1,3,5 *", "Every minute, in January, March, and May.")]
    [InlineData("* * * * 5L", "Every minute, on the last Friday of the month.")]
    [InlineData("* * * * TUE#3", "Every minute, on the 3rd Tuesday of the month.")]
    [InlineData("* * 15W * *", "Every minute, on the weekday nearest day 15.")]
    [InlineData("0 0 15 * FRI", "At 00:00, on day 15 or Friday.")]
    [InlineData("30 18 15,L MAR *", "At 18:30, on day 15 and the last day of the month, in March.")]
    [InlineData("* */2 * * * *", "Every second, every 2 minutes.")]
    [InlineData("18-28/2 * * * * *", "At second 18, 20, 22, 24, 26, and 28.")]
    [InlineData("* 0 * 1 *", "Every minute past hour 0, in January.")]
    [InlineData("* 0 * * MON", "Every minute past hour 0, on Monday.")]
    [InlineData("0 0 1 */2 *", "At 00:00, on day 1, in every 2 months.")]
    public void English(string pattern, string expected) => Assert.Equal(expected, Describe(pattern));

    [Fact]
    public void DomAndDow()
    {
        Assert.Equal("At 00:00, on day 15 (if it is also Friday).", Describe("0 0 15 * FRI", domAndDow: true));
        Assert.Equal("At 00:00, on day 15 (if it is also Friday).", Describe("0 0 15 * +FRI"));
        Assert.Equal("At 18:30, on day 15 and the last day of the month (if it is also Friday), in March.",
            Describe("30 18 15,L MAR FRI", domAndDow: true));
        Assert.Equal("At 00:00, on day 1 (if it is also one of: the last Friday of the month and the 1st Monday of the month).",
            Describe("0 0 1 * FRI#L,MON#1", domAndDow: true));
    }

    [Theory]
    [InlineData("* 0 * * *", "Varje minut under timme 0.")]
    [InlineData("* * 0,12 * * *", "Varje sekund under timme 0 och 12.")]
    [InlineData("0 0 1 */2 *", "Klockan 00:00, på dag 1, i var 2:e månad.")]
    [InlineData("0 0 0 1 1 * 2025-2035/2", "Klockan 00:00, på dag 1, i januari, i var 2:e år.")]
    public void Swedish(string pattern, string expected) =>
        Assert.Equal(expected, Cron.Parse(pattern).Describe(Describing.Swedish.Instance));

    private sealed class TwelveHour : ICronLanguage
    {
        private readonly ICronLanguage _en = Describing.English.Instance;
        public string EveryMinute => _en.EveryMinute;
        public string EverySecondPhrase => _en.EverySecondPhrase;
        public string EveryXMinutes(int step) => _en.EveryXMinutes(step);
        public string EveryXSeconds(int step) => _en.EveryXSeconds(step);
        public string EveryXHours(int step) => _en.EveryXHours(step);
        public string AtTime(string time) => _en.AtTime(time);
        public string AtTimeAndEveryXSeconds(string time, int step) => _en.AtTimeAndEveryXSeconds(time, step);
        public string AtTimeAtSecond(string time, string second) => _en.AtTimeAtSecond(time, second);
        public string AtPhrase(string phrase) => _en.AtPhrase(phrase);
        public string OnPhrase(string phrase) => _en.OnPhrase(phrase);
        public string InPhrase(string phrase) => _en.InPhrase(phrase);
        public string SecondPhrase(string s) => _en.SecondPhrase(s);
        public string MinutePhrase(string s) => _en.MinutePhrase(s);
        public string MinutePastEveryHourPhrase(string s) => _en.MinutePastEveryHourPhrase(s);
        public string HourPhrase(string s) => _en.HourPhrase(s);
        public string YearPhrase(string s) => _en.YearPhrase(s);
        public string DayPhrase(string s) => _en.DayPhrase(s);
        public string TheLastDayOfTheMonth => _en.TheLastDayOfTheMonth;
        public string TheWeekdayNearestDay(string day) => _en.TheWeekdayNearestDay(day);
        public string TheLastWeekdayOfTheMonth(string day) => _en.TheLastWeekdayOfTheMonth(day);
        public string TheNthWeekdayOfTheMonth(int n, string day) => _en.TheNthWeekdayOfTheMonth(n, day);
        public string DomAndDowIfAlso(string dow) => _en.DomAndDowIfAlso(dow);
        public string DomAndDowIfAlsoOneOf(string dow) => _en.DomAndDowIfAlsoOneOf(dow);
        public string ListConjunctionAnd => _en.ListConjunctionAnd;
        public string ListConjunctionOr => _en.ListConjunctionOr;
        public string[] DayOfWeekNames => _en.DayOfWeekNames;
        public string[] MonthNames => _en.MonthNames;

        public string TimeOfDay(int hour, int minute, int? second)
        {
            string suffix = hour < 12 ? "AM" : "PM";
            int h = hour % 12 == 0 ? 12 : hour % 12;
            return second is int s ? $"{h}:{minute:00}:{s:00} {suffix}" : $"{h}:{minute:00} {suffix}";
        }
    }

    [Fact]
    public void CustomLanguageCanOverrideTimeFormat()
    {
        Assert.Equal("At 2:00 PM.", Cron.Parse("0 14 * * *").Describe(new TwelveHour()));
        Assert.Equal("At 12:00:30 AM.", Cron.Parse("30 0 0 * * *").Describe(new TwelveHour()));
    }

    [Theory]
    [InlineData("* 0 * * *")]
    [InlineData("* * 0 * * *")]
    [InlineData("0 * 0 * * *")]
    [InlineData("* 0 * 1 *")]
    [InlineData("* 0 * * MON")]
    public void NoGrammaticalErrors(string pattern)
    {
        string desc = Describe(pattern);
        Assert.DoesNotContain("At of", desc);
        Assert.DoesNotContain("At ,", desc);
        Assert.False(desc.StartsWith("At ."));
    }
}
