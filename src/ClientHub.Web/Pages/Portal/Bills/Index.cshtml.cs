using ClientHub.Web.Data.Entities;
using ClientHub.Web.Services.Bills;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Portal.Bills;

/// <summary>Client bills (Q-04): Sent and Paid bills on the client's own cases; unpaid first.</summary>
public class IndexModel : PageModel
{
    private readonly IBillService _bills;
    public IndexModel(IBillService bills) => _bills = bills;

    public List<BillRow> Rows { get; private set; } = new();

    public async Task OnGetAsync()
    {
        var rows = await _bills.ListFor(User).ToListAsync();
        Rows = rows.OrderBy(r => r.Status == BillStatus.Sent ? 0 : 1).ThenByDescending(r => r.IssuedAt).ToList();
    }
}
