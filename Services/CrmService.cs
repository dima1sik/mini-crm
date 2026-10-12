using System.Globalization;
using MiniCRM.Data;
using MiniCRM.Models;

namespace MiniCRM.Services;

public sealed class CrmService(CrmStore store)
{
    public int AddClient(CrmData data, ClientInput input)
    {
        var company = Text(input.Company, "Company name", 200);
        var email = Email(input.Email);
        Owner(input.Owner);
        var client = new Client
        {
            Id = CrmStore.NextId(data),
            CompanyName = company,
            Email = email,
            ContactName = Text(input.Contact, "Contact", 200, optional: true),
            Phone = Text(input.Phone, "Phone", 50, optional: true),
            Owner = input.Owner,
            Created = store.Today
        };
        data.Clients.Insert(0, client);
        return client.Id;
    }

    public int AddNote(CrmData data, AppUser user, int clientId, string text)
    {
        var client = Find(data.Clients, x => x.Id == clientId, "Client");
        var note = Text(text, "Note", 4000);
        client.Notes.Insert(0, new Note { Id = CrmStore.NextId(data), Date = store.Today, Author = user.FullName, Text = note });
        return client.Id;
    }

    public int AddDeal(CrmData data, AppUser user, DealInput input)
    {
        Find(data.Clients, x => x.Id == input.ClientId, "Client");
        var title = Text(input.Title, "Deal title", 200);
        Choice(input.Stage, CrmRules.DealStages, "Stage");
        Owner(input.Owner);
        var deadline = Date(input.Deadline, "Deadline");
        if (!decimal.TryParse(input.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            || value < 0 || value > CrmRules.MaximumDealValue || decimal.Round(value, 2) != value)
            throw Invalid("Value must be a nonnegative amount with at most two decimal places.");
        var deal = new Deal
        {
            Id = CrmStore.NextId(data),
            ClientId = input.ClientId,
            Title = title,
            Stage = input.Stage,
            Owner = input.Owner,
            Value = value,
            Deadline = deadline,
            Created = store.Today,
            LastActivityDate = store.Today,
            History = [new DealHistory { Date = store.Today, Author = user.FullName, Text = $"Deal created in {input.Stage} stage." }]
        };
        data.Deals.Insert(0, deal);
        return deal.Id;
    }

    public int UpdateDeal(CrmData data, AppUser user, int id, string stage, string owner)
    {
        var deal = Find(data.Deals, x => x.Id == id, "Deal");
        Choice(stage, CrmRules.DealStages, "Stage");
        if (!string.IsNullOrWhiteSpace(owner))
        {
            Owner(owner);
            if (owner != deal.Owner && user.RoleId is not "admin" and not "manager")
                throw new CrmException(403, "Only administrators and managers can reassign a deal.");
        }
        if (stage != deal.Stage)
        {
            deal.History.Insert(0, new DealHistory { Date = store.Today, Author = user.FullName, Text = $"Stage changed: {deal.Stage} -> {stage}." });
            deal.Stage = stage;
            deal.LastActivityDate = store.Today;
        }
        if (!string.IsNullOrWhiteSpace(owner) && owner != deal.Owner)
        {
            var old = deal.Owner;
            deal.Owner = owner;
            deal.LastActivityDate = store.Today;
            deal.History.Insert(0, new DealHistory { Date = store.Today, Author = user.FullName, Text = $"Owner changed: {old} -> {owner}." });
            AddAudit(data, user, "Deal owner changed", deal.Title, $"{old} -> {owner}");
        }
        return deal.Id;
    }

    public int AddTask(CrmData data, TaskInput input)
    {
        var deal = Find(data.Deals, x => x.Id == input.DealId, "Deal");
        var title = Text(input.Title, "Task title", 200);
        var due = Date(input.DueDate, "Due date");
        Choice(input.Priority, CrmRules.Priorities, "Priority");
        Owner(input.Owner);
        var task = new FollowUpTask
        {
            Id = CrmStore.NextId(data),
            DealId = deal.Id,
            ClientId = deal.ClientId,
            Title = title,
            DueDate = due,
            Priority = input.Priority,
            Owner = input.Owner,
            Status = "Open"
        };
        data.Tasks.Insert(0, task);
        return task.Id;
    }

    public void UpdateTask(CrmData data, int id, string status)
    {
        var task = Find(data.Tasks, x => x.Id == id, "Task");
        Choice(status, CrmRules.TaskStatuses, "Status");
        task.Status = status;
    }

    public void AddUser(CrmData data, AppUser actor, UserInput input)
    {
        var first = Text(input.FirstName, "First name", 100);
        var last = Text(input.LastName, "Last name", 100);
        var email = Email(input.Email);
        var role = data.Roles.FirstOrDefault(r => r.Id == input.RoleId) ?? throw Invalid("Choose an existing role.");
        if (data.Users.Any(u => u.Email.Equals(email, StringComparison.OrdinalIgnoreCase))) throw Invalid("A user already has this email address.");
        var user = new AppUser { Id = CrmStore.NextId(data), FirstName = first, LastName = last, Email = email, RoleId = role.Id };
        data.Users.Insert(0, user);
        AddAudit(data, actor, "User added", user.FullName, $"Role: {role.Name}");
    }

    public void ChangeUserRole(CrmData data, AppUser actor, int id, string roleId)
    {
        var user = Find(data.Users, x => x.Id == id, "User");
        if (!data.Roles.Any(r => r.Id == roleId)) throw Invalid("Choose an existing role.");
        if (user.RoleId == roleId) return;
        if (roleId != "admin") ProtectLastAdmin(data, user);
        var old = user.RoleId;
        user.RoleId = roleId;
        AddAudit(data, actor, "User role changed", user.FullName, $"{old} -> {roleId}");
    }

    public void ToggleUser(CrmData data, AppUser actor, int id)
    {
        var user = Find(data.Users, x => x.Id == id, "User");
        if (user.Status == "Active") ProtectLastAdmin(data, user);
        var old = user.Status;
        user.Status = old == "Active" ? "Blocked" : "Active";
        AddAudit(data, actor, user.Status == "Blocked" ? "User blocked" : "User unblocked", user.FullName, $"{old} -> {user.Status}");
    }

    public void ToggleModule(CrmData data, AppUser actor, string roleId, string module)
    {
        var role = Find(data.Roles, x => x.Id == roleId, "Role");
        if (!CrmRules.Modules.Any(m => m.Id == module)) throw Invalid("Choose an existing module.");
        if (role.Id == "admin" && module == "admin") throw Invalid("Administrators must retain access to settings.");
        var removed = role.Modules.Remove(module);
        if (!removed) role.Modules.Add(module);
        AddAudit(data, actor, "Role permissions changed", role.Name, $"{(removed ? "Removed" : "Added")} module: {module}");
    }

    public void SaveView(CrmData data, string name, string from, string to, string owner)
    {
        name = Text(name, "Saved view name", 100);
        Date(from, "Start date"); Date(to, "End date");
        if (string.CompareOrdinal(from, to) > 0) throw Invalid("Start date must not be later than end date.");
        if (owner != "All") Owner(owner);
        data.SavedViews.Insert(0, new SavedView { Id = CrmStore.NextId(data), Name = name, DateFrom = from, DateTo = to, Owner = owner });
    }

    private void AddAudit(CrmData data, AppUser actor, string action, string target, string details) => data.Audit.Insert(0,
        new AuditEntry { Id = CrmStore.NextId(data), Date = store.Timestamp, Admin = actor.FullName, Action = action, Target = target, Details = details });

    private static void ProtectLastAdmin(CrmData data, AppUser user)
    {
        if (user.RoleId == "admin" && user.Status == "Active" && !data.Users.Any(u => u.Id != user.Id && u.RoleId == "admin" && u.Status == "Active"))
            throw Invalid("At least one active administrator must remain.");
    }
    private static T Find<T>(IEnumerable<T> source, Func<T, bool> match, string name) => source.FirstOrDefault(match) ?? throw new CrmException(404, $"{name} not found.");
    private static CrmException Invalid(string message) => new(400, message);
    private static string Text(string value, string name, int maxLength, bool optional = false)
    {
        value = value.Trim();
        if ((!optional && value.Length == 0) || value.Length > maxLength || !CrmRules.IsSupportedText(value)
            || value.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            throw Invalid($"{name} must contain {(optional ? "at most" : "1 to")} {maxLength} characters.");
        return value;
    }
    private static string Email(string email)
    {
        email = email.Trim();
        if (!CrmRules.IsEmail(email)) throw Invalid("Enter a valid email address.");
        return email;
    }
    private static string Date(string value, string name)
    {
        if (!CrmRules.IsDate(value)) throw Invalid($"{name} must be a valid date in yyyy-MM-dd format.");
        return value;
    }
    private static void Owner(string owner) => Choice(owner, CrmRules.OwnerNames, "Owner");
    private static void Choice(string value, IReadOnlyList<string> choices, string name)
    {
        if (!choices.Contains(value)) throw Invalid($"Choose an existing {name.ToLowerInvariant()}.");
    }
}

public record ClientInput(string Company, string Contact, string Phone, string Email, string Owner);
public record DealInput(int ClientId, string Title, string Stage, string Value, string Deadline, string Owner);
public record TaskInput(int DealId, string Title, string DueDate, string Priority, string Owner);
public record UserInput(string FirstName, string LastName, string Email, string RoleId);
