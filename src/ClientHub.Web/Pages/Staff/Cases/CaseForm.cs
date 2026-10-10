using System.ComponentModel.DataAnnotations;
using ClientHub.Web.Data.Entities;

namespace ClientHub.Web.Pages.Staff.Cases;

/// <summary>Case fields (R-034). LawyerId and StaffIds are only read for Lawyer and Office Manager (Q-10).</summary>
public class CaseForm
{
    [Required(ErrorMessage = "Validation.Required"), StringLength(200, ErrorMessage = "Validation.TooLong")]
    public string? Title { get; set; }
    [Required(ErrorMessage = "Validation.Required")]
    public string CaseType { get; set; } = Data.Entities.CaseType.Civil;
    [StringLength(4000, ErrorMessage = "Validation.TooLong")]
    public string? Description { get; set; }
    public Guid? LawyerId { get; set; }
    public List<Guid> StaffIds { get; set; } = new();
}

public record Choice(Guid Id, string Name);

public record CaseFieldsModel(CaseForm Form, bool CanAssign, IReadOnlyList<Choice> Lawyers, IReadOnlyList<Choice> Staff);
