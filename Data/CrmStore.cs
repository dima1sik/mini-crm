using System.Text;
using System.Text.Json;
using MiniCRM.Models;

namespace MiniCRM.Data;

public class CrmStore
{
    public const string Today = "2026-01-15";
    public const decimal SalesGoal = 18000m;
    private readonly string _path;
    public CrmData Data { get; private set; }
    public static readonly List<ModuleDef> Modules =
    [
        new("dashboard", "Dashboard"),
        new("clients", "Clients / Client 360"),
        new("deals", "Deals Pipeline"),
        new("tasks", "Tasks / Follow-up"),
        new("reports", "Reports"),
        new("admin", "Admin / Settings"),
    ];
    public static readonly List<string> DealStages = ["New", "Contacted", "Quoted", "Negotiation", "Won", "Lost"];
    public static readonly List<string> TaskStatuses = ["Open", "In progress", "Done", "Overdue"];
    public static readonly List<string> OwnerNames = ["Anna Sales", "Daniel Sales", "Julia Manager"];

    public CrmStore(IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "crm-data.json");
        Data = Load();
    }

    public int NextId()
    {
        var ids = new List<int>();
        ids.AddRange(Data.Users.Select(x => x.Id));
        ids.AddRange(Data.Clients.Select(x => x.Id));
        ids.AddRange(Data.Deals.Select(x => x.Id));
        ids.AddRange(Data.Tasks.Select(x => x.Id));
        ids.AddRange(Data.Audit.Select(x => x.Id));
        ids.AddRange(Data.SavedViews.Select(x => x.Id));
        return ids.Count == 0 ? 1 : ids.Max() + 1;
    }

    public void AddAudit(string admin, string action, string target, string details)
    {
        Data.Audit.Insert(0, new AuditEntry
        {
            Id = NextId(),
            Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Admin = admin,
            Action = action,
            Target = target,
            Details = details
        });
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json, Encoding.UTF8);
    }

    public void Reset()
    {
        Data = Seed();
        Save();
    }

    private CrmData Load()
    {
        if (!File.Exists(_path))
        {
            var seed = Seed();
            Data = seed;
            Save();
            return seed;
        }
        try
        {
            var json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<CrmData>(json) ?? Seed();
        }
        catch
        {
            return Seed();
        }
    }

    private static CrmData Seed() => new()
    {
        Users =
        [
            new AppUser { Id = 1, FirstName = "Dmytro", LastName = "Nesvitailo", Email = "dn91631@student.uwb.edu.pl", RoleId = "admin", Status = "Active" },
            new AppUser { Id = 2, FirstName = "Marta", LastName = "Admin", Email = "marta.admin@crm.test", RoleId = "admin", Status = "Active" },
            new AppUser { Id = 3, FirstName = "Anna", LastName = "Sales", Email = "anna.sales@crm.test", RoleId = "sales", Status = "Active" },
            new AppUser { Id = 4, FirstName = "Julia", LastName = "Manager", Email = "julia.manager@crm.test", RoleId = "manager", Status = "Active" },
            new AppUser { Id = 5, FirstName = "Marek", LastName = "Director", Email = "marek.director@crm.test", RoleId = "director", Status = "Active" },
        ],
        Roles =
        [
            new RoleDef { Id = "admin", Name = "Administrator systemu", Modules = ["dashboard", "clients", "deals", "tasks", "reports", "admin"] },
            new RoleDef { Id = "sales", Name = "Handlowiec", Modules = ["dashboard", "clients", "deals", "tasks"] },
            new RoleDef { Id = "manager", Name = "Manager sprzedazy", Modules = ["dashboard", "clients", "deals", "tasks", "reports"] },
            new RoleDef { Id = "director", Name = "Dyrektor handlowy / Zarzad", Modules = ["dashboard", "deals", "reports"] },
        ],
        Clients =
        [
            new Client { Id = 10, CompanyName = "BlueSoft Sp. z o.o.", ContactName = "Piotr Zielinski", Phone = "503 456 789", Email = "piotr@bluesoft.test", Owner = "Anna Sales", Created = "2026-01-08", Notes = [new Note { Id = 101, Date = "2026-01-08", Author = "Anna Sales", Text = "Client asked for phone offer." }, new Note { Id = 102, Date = "2026-01-11", Author = "Anna Sales", Text = "Follow-up planned this week." }] },
            new Client { Id = 11, CompanyName = "TechVision S.A.", ContactName = "Jan Kowalski", Phone = "502 345 678", Email = "jan@techvision.test", Owner = "Daniel Sales", Created = "2026-01-10", Notes = [new Note { Id = 103, Date = "2026-01-10", Author = "Daniel Sales", Text = "Interested in laptop package." }] },
            new Client { Id = 12, CompanyName = "GreenHouse S.A.", ContactName = "Marta Sikora", Phone = "504 567 890", Email = "marta@greenhouse.test", Owner = "Anna Sales", Created = "2026-01-11", Notes = [] },
        ],
        Deals =
        [
            new Deal { Id = 201, ClientId = 10, Title = "iPhone 15 package", Stage = "Quoted", Value = 3499, Deadline = "2026-01-20", Owner = "Anna Sales", Created = "2026-01-08", LastActivityDate = "2026-01-12", History = [new DealHistory { Date = "2026-01-12", Author = "Anna Sales", Text = "Stage changed: Contacted -> Quoted." }, new DealHistory { Date = "2026-01-08", Author = "Anna Sales", Text = "Deal created in New stage." }] },
            new Deal { Id = 202, ClientId = 11, Title = "MacBook Air for TechVision", Stage = "Negotiation", Value = 5299, Deadline = "2026-01-23", Owner = "Daniel Sales", Created = "2026-01-10", LastActivityDate = "2026-01-04", History = [new DealHistory { Date = "2026-01-10", Author = "Daniel Sales", Text = "Deal created." }] },
            new Deal { Id = 203, ClientId = 12, Title = "Accessory bundle", Stage = "Won", Value = 1600, Deadline = "2026-01-14", Owner = "Anna Sales", Created = "2026-01-11", LastActivityDate = "2026-01-14", History = [new DealHistory { Date = "2026-01-14", Author = "Anna Sales", Text = "Stage changed: Negotiation -> Won." }] },
            new Deal { Id = 204, ClientId = 10, Title = "Samsung S24 order", Stage = "Contacted", Value = 3899, Deadline = "2026-01-28", Owner = "Anna Sales", Created = "2026-01-14", LastActivityDate = "2026-01-14", History = [new DealHistory { Date = "2026-01-14", Author = "Anna Sales", Text = "Deal created after phone call." }] },
            new Deal { Id = 205, ClientId = 10, Title = "MacBook Air for office team", Stage = "Contacted", Value = 12000, Deadline = "2026-01-15", Owner = "Anna Sales", Created = "2026-01-15", LastActivityDate = "2026-01-15", History = [new DealHistory { Date = "2026-01-15", Author = "Anna Sales", Text = "Deal created in New stage." }] },
        ],
        Tasks =
        [
            new FollowUpTask { Id = 301, Title = "Call Piotr about final decision", DealId = 201, ClientId = 10, DueDate = "2026-01-16", Priority = "High", Owner = "Anna Sales", Status = "Open" },
            new FollowUpTask { Id = 302, Title = "Send updated MacBook quote", DealId = 202, ClientId = 11, DueDate = "2026-01-11", Priority = "High", Owner = "Daniel Sales", Status = "Overdue" },
            new FollowUpTask { Id = 303, Title = "Prepare invoice for accessories", DealId = 203, ClientId = 12, DueDate = "2026-01-14", Priority = "Medium", Owner = "Anna Sales", Status = "Done" },
        ],
        Audit = [new AuditEntry { Id = 401, Date = "2026-01-10 09:15", Admin = "Marta Admin", Action = "Initial setup", Target = "System", Details = "Default roles and demo data created." }],
        SavedViews = [new SavedView { Id = 501, Name = "All sales this month", DateFrom = "2026-01-01", DateTo = "2026-01-31", Owner = "All" }]
    };
}

