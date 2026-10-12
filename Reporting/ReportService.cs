using System.Globalization;
using MiniCRM.Models;
using MiniCRM.Services;

namespace MiniCRM.Reporting;

public record ReportFilters(string From, string To, string Owner);

public static class ReportService
{
    public static ReportFilters Filters(HttpContext context, string today)
    {
        var date = DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var from = context.Request.Query["from"].ToString();
        var to = context.Request.Query["to"].ToString();
        var owner = context.Request.Query["owner"].ToString();
        if (string.IsNullOrWhiteSpace(from)) from = new DateOnly(date.Year, date.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(to)) to = new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(owner)) owner = "All";
        if (!CrmRules.IsDate(from) || !CrmRules.IsDate(to) || string.CompareOrdinal(from, to) > 0)
            throw new CrmException(400, "Choose a valid date range.");
        if (owner != "All" && !CrmRules.OwnerNames.Contains(owner)) throw new CrmException(400, "Choose an existing salesperson.");
        return new(from, to, owner);
    }

    public static IEnumerable<Deal> Deals(CrmData data, ReportFilters filter) => data.Deals.Where(d =>
        string.CompareOrdinal(d.Created, filter.From) >= 0 && string.CompareOrdinal(d.Created, filter.To) <= 0
        && (filter.Owner == "All" || d.Owner == filter.Owner));

    public static decimal PreviousRevenue(CrmData data, ReportFilters filter)
    {
        var start = DateOnly.ParseExact(filter.From, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var finish = DateOnly.ParseExact(filter.To, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        // The immediately preceding period has the same inclusive number of days.
        var days = finish.DayNumber - start.DayNumber + 1;
        var previousTo = start.DayNumber - 1;
        var previousFrom = previousTo - days + 1;
        return data.Deals.Where(d => d.Stage == "Won" && (filter.Owner == "All" || d.Owner == filter.Owner)
            && DateOnly.ParseExact(d.Created, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber >= previousFrom
            && DateOnly.ParseExact(d.Created, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber <= previousTo).Sum(d => d.Value);
    }

    public static string Query(string from, string to, string owner) =>
        $"from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}&owner={Uri.EscapeDataString(owner)}";

    public static string Money(decimal value) => $"PLN {value.ToString("N2", CultureInfo.InvariantCulture)}";

    public static List<string> Lines(CrmData data, ReportFilters filter, string timestamp)
    {
        var deals = Deals(data, filter).ToList();
        var lines = new List<string> { "MiniCRM sales report", $"Generated (UTC): {timestamp}", $"Period: {filter.From} - {filter.To}",
            $"Owner: {filter.Owner}", "", $"Won revenue: {Money(deals.Where(d => d.Stage == "Won").Sum(d => d.Value))}",
            $"Open pipeline: {Money(deals.Where(d => d.Stage is not "Won" and not "Lost").Sum(d => d.Value))}",
            $"Active deals: {deals.Count(d => d.Stage is not "Won" and not "Lost")}", "", "Deals:" };
        lines.AddRange(deals.Select(d => $"{d.Title} | {d.Owner} | {d.Stage} | {Money(d.Value)}"));
        return lines;
    }
}
