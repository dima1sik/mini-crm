using System.Text.Json;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using MiniCRM.Models;

namespace MiniCRM.Data;

/// <summary>A single-process local store. Readers get snapshots; changes commit atomically.</summary>
public sealed class CrmStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly string _path;
    private readonly TimeProvider _clock;
    private CrmData _data;

    public CrmStore(IWebHostEnvironment environment, IConfiguration configuration, TimeProvider clock)
    {
        _clock = clock;
        _path = Path.GetFullPath(configuration["Crm:DataPath"]
            ?? Path.Combine(environment.ContentRootPath, "App_Data", "crm-data.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (File.Exists(_path))
        {
            // Do not replace corrupt or unreadable user data with demo data.
            _data = JsonSerializer.Deserialize<CrmData>(File.ReadAllText(_path), JsonOptions)
                ?? throw new InvalidDataException("CRM data is empty. Restore a valid backup before starting.");
            Validate(_data);
        }
        else
        {
            _data = DemoData.Create(Today);
            Persist(_data);
        }
    }

    public string Today => _clock.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string Timestamp => _clock.GetUtcNow().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public CrmData Read()
    {
        lock (_gate) return Clone(_data);
    }

    public T Change<T>(Func<CrmData, T> change)
    {
        lock (_gate)
        {
            var candidate = Clone(_data);
            var result = change(candidate);
            Validate(candidate);
            Persist(candidate);
            // Neither the caller's candidate nor its return value can mutate live state.
            _data = Clone(candidate);
            return result;
        }
    }

    public void Reset() => Change(data =>
    {
        var seed = DemoData.Create(Today);
        data.Users = seed.Users; data.Roles = seed.Roles; data.Clients = seed.Clients;
        data.Deals = seed.Deals; data.Tasks = seed.Tasks; data.Audit = seed.Audit;
        data.SavedViews = seed.SavedViews;
        return true;
    });

    public static int NextId(CrmData data) => checked(data.Users.Select(x => x.Id)
        .Concat(data.Clients.Select(x => x.Id)).Concat(data.Clients.SelectMany(x => x.Notes).Select(x => x.Id))
        .Concat(data.Deals.Select(x => x.Id)).Concat(data.Tasks.Select(x => x.Id))
        .Concat(data.Audit.Select(x => x.Id)).Concat(data.SavedViews.Select(x => x.Id))
        .DefaultIfEmpty(0).Max() + 1);

    private static CrmData Clone(CrmData data) =>
        JsonSerializer.Deserialize<CrmData>(JsonSerializer.Serialize(data, JsonOptions), JsonOptions)!;

    private void Persist(CrmData data)
    {
        // Same-directory rename replaces the old file only after a complete flushed write.
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Validate(CrmData data)
    {
        void Require([DoesNotReturnIf(false)] bool valid) { if (!valid) throw new InvalidDataException("CRM data has an invalid schema. Restore a valid backup before starting."); }
        Require(data.Users is not null && data.Roles is not null && data.Clients is not null
            && data.Deals is not null && data.Tasks is not null && data.Audit is not null && data.SavedViews is not null);
        Require(data.Users!.All(x => x is not null) && data.Roles!.All(x => x is not null)
            && data.Clients!.All(x => x is not null) && data.Deals!.All(x => x is not null)
            && data.Tasks!.All(x => x is not null) && data.Audit!.All(x => x is not null) && data.SavedViews!.All(x => x is not null));
        Require(data.Roles!.All(r => r.Modules is not null && r.Modules.All(m => CrmRules.Modules.Any(x => x.Id == m)))
            && data.Roles.Select(r => r.Id).Distinct().Count() == data.Roles.Count);
        Require(data.Users!.Count > 0 && data.Users.All(u => data.Roles.Any(r => r.Id == u.RoleId)
            && u.Status is "Active" or "Blocked" && !string.IsNullOrWhiteSpace(u.FirstName) && !string.IsNullOrWhiteSpace(u.LastName) && CrmRules.IsEmail(u.Email)));
        Require(data.Users.Any(u => u.Status == "Active" && u.RoleId == "admin")
            && data.Roles.Any(r => r.Id == "admin" && r.Modules.Contains("admin")));
        Require(data.Clients!.All(c => c.Notes is not null && c.Notes.All(n => n is not null && CrmRules.IsDate(n.Date) && n.Text is not null && n.Author is not null)
            && CrmRules.IsDate(c.Created) && !string.IsNullOrWhiteSpace(c.CompanyName) && CrmRules.IsEmail(c.Email)
            && c.ContactName is not null && c.Phone is not null && CrmRules.OwnerNames.Contains(c.Owner)));
        Require(data.Deals!.All(d => data.Clients.Any(c => c.Id == d.ClientId) && CrmRules.DealStages.Contains(d.Stage)
            && d.Value is >= 0 and <= CrmRules.MaximumDealValue && CrmRules.IsDate(d.Created) && CrmRules.IsDate(d.LastActivityDate)
            && CrmRules.IsDate(d.Deadline) && !string.IsNullOrWhiteSpace(d.Title) && CrmRules.IsSupportedText(d.Title) && CrmRules.OwnerNames.Contains(d.Owner)
            && d.History is not null && d.History.All(h => h is not null && CrmRules.IsDate(h.Date) && h.Author is not null && h.Text is not null)));
        Require(data.Tasks!.All(t => data.Deals.Any(d => d.Id == t.DealId && d.ClientId == t.ClientId)
            && CrmRules.IsDate(t.DueDate) && (CrmRules.TaskStatuses.Contains(t.Status) || t.Status == "Overdue") && CrmRules.Priorities.Contains(t.Priority)
            && CrmRules.OwnerNames.Contains(t.Owner) && !string.IsNullOrWhiteSpace(t.Title)));
        Require(data.SavedViews!.All(v => CrmRules.IsDate(v.DateFrom) && CrmRules.IsDate(v.DateTo)
            && string.CompareOrdinal(v.DateFrom, v.DateTo) <= 0 && (v.Owner == "All" || CrmRules.OwnerNames.Contains(v.Owner)) && !string.IsNullOrWhiteSpace(v.Name)));
        Require(data.Audit!.All(a => a.Date is not null && a.Admin is not null && a.Action is not null && a.Target is not null && a.Details is not null));
        var ids = data.Users.Select(x => x.Id).Concat(data.Clients.Select(x => x.Id))
            .Concat(data.Clients.SelectMany(x => x.Notes).Select(x => x.Id)).Concat(data.Deals.Select(x => x.Id))
            .Concat(data.Tasks.Select(x => x.Id)).Concat(data.Audit.Select(x => x.Id)).Concat(data.SavedViews.Select(x => x.Id)).ToList();
        Require(ids.All(id => id > 0) && ids.Count == ids.Distinct().Count());
    }
}
