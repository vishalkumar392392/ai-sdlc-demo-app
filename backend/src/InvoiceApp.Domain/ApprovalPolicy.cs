using System.Globalization;

namespace InvoiceApp.Domain;

/// <summary>
/// Decides whether a user in a given role may approve an invoice, and what the approval does.
/// Pure: it reads the invoice (including its recorded approvals) but never changes it.
/// Limits are inclusive: a Manager may approve exactly 50,000.00. Over 500,000.00 two distinct
/// approvers are needed; the first may be a Manager or a Director, the second must be a Director.
/// </summary>
public class ApprovalPolicy
{
    public const decimal ManagerLimit = 50_000m;
    public const decimal DirectorLimit = 500_000m;

    private static readonly IReadOnlyDictionary<string, decimal> LimitsByRole = new Dictionary<string, decimal>
    {
        [ApprovalRoles.Manager] = ManagerLimit,
        [ApprovalRoles.Director] = DirectorLimit,
    };

    /// <summary>
    /// Returns what the approval does, or throws <see cref="ApprovalNotAllowedException"/> with the reason
    /// when the approval is not allowed.
    /// </summary>
    public ApprovalOutcome EnsureCanApprove(Invoice invoice, string approver, string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ApprovalNotAllowedException(
                $"Your account has no role that can approve invoices. Approvals need a Manager (up to {Money(ManagerLimit)}) or a Director (up to {Money(DirectorLimit)}).");
        }

        if (!LimitsByRole.TryGetValue(role, out var limit))
        {
            throw new ApprovalNotAllowedException(
                $"The {role} role cannot approve invoices. Approvals need a Manager (up to {Money(ManagerLimit)}) or a Director (up to {Money(DirectorLimit)}).");
        }

        if (invoice.Amount > DirectorLimit)
        {
            return EnsureCanApproveAboveDirectorLimit(invoice, approver, role);
        }

        if (invoice.Amount > limit)
        {
            throw new ApprovalNotAllowedException(
                $"{role} approval limit is {Money(limit)}; invoice {invoice.Id} is {Money(invoice.Amount)} and needs a Director.");
        }

        // A single approval is enough here; still, nobody approves the same invoice twice.
        EnsureNotAlreadyApprovedBy(invoice, approver, "another approver must approve it");
        return ApprovalOutcome.ApproveFinal;
    }

    private static ApprovalOutcome EnsureCanApproveAboveDirectorLimit(Invoice invoice, string approver, string role)
    {
        if (invoice.Approvals.Count == 0)
        {
            return ApprovalOutcome.RecordFirstApproval;
        }

        if (role != ApprovalRoles.Director)
        {
            throw new ApprovalNotAllowedException(
                $"{role} cannot give the second approval: invoice {invoice.Id} is {Money(invoice.Amount)}, over {Money(DirectorLimit)}, and its second approval must come from a Director.");
        }

        EnsureNotAlreadyApprovedBy(invoice, approver, "the second approval must come from a different Director");
        return ApprovalOutcome.ApproveFinal;
    }

    private static void EnsureNotAlreadyApprovedBy(Invoice invoice, string approver, string whatIsNeeded)
    {
        if (invoice.Approvals.Any(record => IsSameUser(record.Approver, approver)))
        {
            throw new ApprovalNotAllowedException(
                $"{approver.Trim()} has already approved invoice {invoice.Id}; {whatIsNeeded}.");
        }
    }

    /// <summary>Usernames match after trimming and ignoring case: " Demo.Director " is demo.director.</summary>
    private static bool IsSameUser(string first, string second) =>
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
