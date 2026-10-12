using MiniCRM.Models;
using MiniCRM.Reporting;
using Xunit;

namespace MiniCRM.Tests;

public sealed class ReportTests
{
    [Theory]
    [InlineData("All", 800)]
    [InlineData("Anna Sales", 300)]
    public void Previous_revenue_uses_creation_dates_and_owner_with_inclusive_equal_length_period(string owner, int expected)
    {
        var data = new CrmData
        {
            Deals =
            [
                Sale("2026-10-06", 100),
                Sale("2026-10-08", 200),
                Sale("2026-10-07", 500, owner: "Daniel Sales"),
                Sale("2026-10-05", 3000),
                Sale("2026-10-09", 4000),
                Sale("2026-10-07", 600, stage: "New"),
                Sale("2026-10-07", 700, stage: "Lost")
            ]
        };

        var revenue = ReportService.PreviousRevenue(data, new ReportFilters("2026-10-09", "2026-10-11", owner));

        Assert.Equal(expected, revenue);
    }

    [Fact]
    public void Previous_period_before_the_first_supported_date_has_no_revenue()
    {
        var data = new CrmData { Deals = [Sale("0001-01-01", 100)] };

        var revenue = ReportService.PreviousRevenue(data, new ReportFilters("0001-01-01", "0001-01-03", "All"));

        Assert.Equal(0, revenue);
    }

    private static Deal Sale(string created, decimal value, string owner = "Anna Sales", string stage = "Won") => new()
    {
        Created = created, Value = value, Owner = owner, Stage = stage,
        Deadline = "2026-10-10", LastActivityDate = "2026-10-09"
    };
}
