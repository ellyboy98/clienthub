using Microsoft.AspNetCore.Identity;

namespace ClientHub.Web.Data.Entities;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public string Role { get; set; } = Roles.Staff;
    public string Language { get; set; } = "en";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AppRole : IdentityRole<Guid>
{
    public AppRole() { }
    public AppRole(string name) : base(name) { }
}

public static class Roles
{
    public const string Lawyer = "Lawyer";
    public const string OfficeManager = "OfficeManager";
    public const string Staff = "Staff";
    public const string Client = "Client";
    public static readonly string[] All = { Lawyer, OfficeManager, Staff, Client };
}

public static class ClientType { public const string Person = "Person", Company = "Company"; }
public static class CaseType { public const string Criminal = "Criminal", Civil = "Civil"; }
public static class CaseStatus
{
    public const string New = "New", DocumentsCollected = "DocumentsCollected", Filed = "Filed",
        Hearing = "Hearing", Completed = "Completed", Cancelled = "Cancelled";
}
public static class RequestStatus { public const string Pending = "Pending", Received = "Received", Rejected = "Rejected"; }
public static class BillStatus { public const string Draft = "Draft", Sent = "Sent", Paid = "Paid"; }
public static class LineType { public const string Fee = "Fee", Expense = "Expense"; }
