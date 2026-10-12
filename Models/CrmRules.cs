using System.Globalization;
using System.Net.Mail;
using System.Xml;

namespace MiniCRM.Models;

public static class CrmRules
{
    public const decimal SalesGoal = 18000m;
    public const decimal MaximumDealValue = 1_000_000_000_000m;
    public static readonly IReadOnlyList<ModuleDef> Modules = Array.AsReadOnly<ModuleDef>([
        new("dashboard", "Dashboard"), new("clients", "Clients / Client 360"),
        new("deals", "Deals Pipeline"), new("tasks", "Tasks / Follow-up"),
        new("reports", "Reports"), new("admin", "Admin / Settings")]);
    public static readonly IReadOnlyList<string> DealStages = Array.AsReadOnly(["New", "Contacted", "Quoted", "Negotiation", "Won", "Lost"]);
    public static readonly IReadOnlyList<string> TaskStatuses = Array.AsReadOnly(["Open", "In progress", "Done"]);
    public static readonly IReadOnlyList<string> Priorities = Array.AsReadOnly(["Low", "Medium", "High"]);
    public static readonly IReadOnlyList<string> OwnerNames = Array.AsReadOnly(["Anna Sales", "Daniel Sales", "Julia Manager"]);
    public static bool IsDate(string? value) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    public static bool IsEmail(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 254
        && MailAddress.TryCreate(value, out var address) && address.Address == value && address.Host.Contains('.');
    public static bool IsSupportedText(string value)
    {
        try
        {
            XmlConvert.VerifyXmlChars(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
    public static string TaskStatus(FollowUpTask task, string today) => task.Status == "Done" ? "Done"
        : string.CompareOrdinal(task.DueDate, today) < 0 ? "Overdue" : task.Status == "Overdue" ? "Open" : task.Status;
}
