namespace ClientHub.Web.Data.Entities;

/// <summary>A move the bill rules do not allow (line edit on a Sent/Paid bill, Paid to Sent, sending with no lines).</summary>
public class BillRuleException : InvalidOperationException
{
    public BillRuleException(string message) : base(message) { }
}

/// <summary>
/// Bill with frozen lines once sent (R-077..R-082). The total is computed only in <see cref="Recalculate"/>; status changes
/// only through <see cref="MarkSent"/> and <see cref="MarkPaid"/>, which throw on invalid moves.
/// Rounding rule: every line amount is rounded to 2 decimals (half away from zero) when it is set; the total is the exact
/// sum of the rounded line amounts, so it always equals what the client sees line by line.
/// </summary>
public class Bill : Entity
{
    /// <summary>Largest value numeric(12,2) holds.</summary>
    public const decimal MaxAmount = 9_999_999_999.99m;
    public const int DescriptionMax = 500;
    public const int NoteMax = 2000;

    public Guid CaseId { get; set; }
    public string BillNo { get; set; } = "";
    public string Status { get; set; } = BillStatus.Draft;
    public string? NoteToClient { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public decimal Total { get; set; }
    public List<BillLine> Lines { get; set; } = new();

    public bool IsDraft => Status == BillStatus.Draft;

    public static decimal RoundAmount(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    /// <summary>A line amount must be above zero and fit numeric(12,2) after rounding.</summary>
    public static bool IsValidAmount(decimal amount)
    {
        var r = RoundAmount(amount);
        return r > 0 && r <= MaxAmount;
    }

    public static bool IsValidLineType(string? type) => type is LineType.Fee or LineType.Expense;

    /// <summary>The only place the total is computed.</summary>
    public void Recalculate()
    {
        var total = Lines.Sum(l => RoundAmount(l.Amount));
        if (total > MaxAmount) throw new BillRuleException("Bill total is too large.");
        Total = total;
    }

    public BillLine AddLine(string lineType, string description, decimal amount)
    {
        EnsureDraft();
        CheckLine(lineType, description, amount);
        var amt = RoundAmount(amount);
        CheckTotalFits(Lines.Sum(l => RoundAmount(l.Amount)) + amt);
        var line = new BillLine
        {
            BillId = Id, LineType = lineType, Description = description.Trim(), Amount = amt,
            SortOrder = Lines.Count == 0 ? 1 : Lines.Max(l => l.SortOrder) + 1
        };
        Lines.Add(line);
        Recalculate();
        return line;
    }

    public void UpdateLine(Guid lineId, string lineType, string description, decimal amount)
    {
        EnsureDraft();
        var line = Lines.FirstOrDefault(l => l.Id == lineId) ?? throw new BillRuleException("Line not found.");
        CheckLine(lineType, description, amount);
        var amt = RoundAmount(amount);
        CheckTotalFits(Lines.Where(l => l.Id != lineId).Sum(l => RoundAmount(l.Amount)) + amt);
        line.LineType = lineType;
        line.Description = description.Trim();
        line.Amount = amt;
        Recalculate();
    }

    /// <summary>Removes the line from the bill; the caller also deletes the row (lines are not records to keep, R-077).</summary>
    public BillLine RemoveLine(Guid lineId)
    {
        EnsureDraft();
        var line = Lines.FirstOrDefault(l => l.Id == lineId) ?? throw new BillRuleException("Line not found.");
        Lines.Remove(line);
        Recalculate();
        return line;
    }

    public void SetNote(string? note)
    {
        EnsureDraft();
        var n = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (n is { Length: > NoteMax }) throw new ArgumentException("Note too long.", nameof(note));
        NoteToClient = n;
    }

    /// <summary>Draft to Sent (R-080, Q-08): at least one line; issued_at set; lines and note frozen.</summary>
    public void MarkSent(DateTime now)
    {
        if (Status != BillStatus.Draft) throw new BillRuleException($"Cannot send a {Status} bill.");
        if (Lines.Count == 0) throw new BillRuleException("A bill needs at least one line to be sent.");
        Recalculate();
        Status = BillStatus.Sent;
        IssuedAt = now;
    }

    /// <summary>Sent to Paid only (R-081, R-082).</summary>
    public void MarkPaid(DateTime now)
    {
        if (Status != BillStatus.Sent) throw new BillRuleException($"Cannot mark a {Status} bill paid.");
        Status = BillStatus.Paid;
        PaidAt = now;
    }

    private void EnsureDraft()
    {
        if (!IsDraft) throw new BillRuleException("Only a Draft bill can be changed.");
    }

    private static void CheckLine(string lineType, string description, decimal amount)
    {
        if (!IsValidLineType(lineType)) throw new ArgumentException("Unknown line type.", nameof(lineType));
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > DescriptionMax)
            throw new ArgumentException("Description required, up to 500 characters.", nameof(description));
        if (!IsValidAmount(amount)) throw new ArgumentOutOfRangeException(nameof(amount));
    }

    private static void CheckTotalFits(decimal total)
    {
        if (total > MaxAmount) throw new BillRuleException("Bill total is too large.");
    }
}
