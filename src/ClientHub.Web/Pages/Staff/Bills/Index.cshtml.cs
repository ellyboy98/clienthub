using ClientHub.Web.Services.Bills;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClientHub.Web.Pages.Staff.Bills;

/// <summary>Bills list (Q-04): every bill on cases the user can open, newest first, filtered in SQL.</summary>
public class IndexModel : PageModel
{
    private readonly IBillService _bills;
    public IndexModel(IBillService bills) => _bills = bills;

    public List<BillRow> Rows { get; private set; } = new();

    public async Task OnGetAsync() => Rows = await _bills.ListFor(User).ToListAsync();
}
