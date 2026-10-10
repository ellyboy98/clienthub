namespace ClientHub.Web.Data.Entities;

/// <summary>Fee or Expense line. Changed only through <see cref="Bill"/> methods while the bill is Draft.</summary>
public class BillLine : Entity
{
    public Guid BillId { get; set; }
    public string LineType { get; set; } = Entities.LineType.Fee;
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public int SortOrder { get; set; }
}
