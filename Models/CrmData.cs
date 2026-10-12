namespace MiniCRM.Models;

public record ModuleDef(string Id, string Label);

public class CrmData
{
    public List<AppUser> Users { get; set; } = [];
    public List<RoleDef> Roles { get; set; } = [];
    public List<Client> Clients { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
    public List<FollowUpTask> Tasks { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
    public List<SavedView> SavedViews { get; set; } = [];
}

public class AppUser
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string RoleId { get; set; } = "sales";
    public string Status { get; set; } = "Active";
    public string FullName => $"{FirstName} {LastName}";
}

public class RoleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Modules { get; set; } = [];
}

public class Client
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Created { get; set; } = "";
    public List<Note> Notes { get; set; } = [];
}

public class Note
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
}

public class Deal
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Title { get; set; } = "";
    public string Stage { get; set; } = "New";
    public decimal Value { get; set; }
    public string Deadline { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Created { get; set; } = "";
    public string LastActivityDate { get; set; } = "";
    public List<DealHistory> History { get; set; } = [];
}

public class DealHistory
{
    public string Date { get; set; } = "";
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
}

public class FollowUpTask
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int DealId { get; set; }
    public int ClientId { get; set; }
    public string DueDate { get; set; } = "";
    public string Priority { get; set; } = "Medium";
    public string Owner { get; set; } = "";
    public string Status { get; set; } = "Open";
}

public class AuditEntry
{
    public int Id { get; set; }
    public string Date { get; set; } = "";
    public string Admin { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string Details { get; set; } = "";
}

public class SavedView
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DateFrom { get; set; } = "";
    public string DateTo { get; set; } = "";
    public string Owner { get; set; } = "All";
}

