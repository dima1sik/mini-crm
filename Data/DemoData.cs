using MiniCRM.Models;
using System.Globalization;

namespace MiniCRM.Data;

public static class DemoData
{
    public static CrmData Create(string today)
    {
        var data = new CrmData()
        {
            Users =
        [
            new AppUser { Id = 1, FirstName = "Dmytro", LastName = "Nesvitailo", Email = "dmytro@crm.test", RoleId = "admin", Status = "Active" },
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
        // Keep the sample period useful whenever a new local store is created.
        var offset = DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - new DateOnly(2026, 1, 15).DayNumber;
        string Shift(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var client in data.Clients)
        {
            client.Created = Shift(client.Created);
            foreach (var note in client.Notes) note.Date = Shift(note.Date);
        }
        foreach (var deal in data.Deals)
        {
            deal.Created = Shift(deal.Created);
            deal.Deadline = Shift(deal.Deadline);
            deal.LastActivityDate = Shift(deal.LastActivityDate);
            foreach (var history in deal.History) history.Date = Shift(history.Date);
        }
        foreach (var task in data.Tasks) task.DueDate = Shift(task.DueDate);
        foreach (var view in data.SavedViews)
        {
            view.DateFrom = Shift(view.DateFrom);
            view.DateTo = Shift(view.DateTo);
        }
        foreach (var audit in data.Audit) audit.Date = Shift(audit.Date[..10]) + audit.Date[10..];
        return data;
    }
}
