using System.Globalization;

namespace InvoiceApp.Domain;

/// <summary>
/// Decides whether a user in a given role may approve an invoice. Pure: it never changes the invoice.
/// Limits are inclusive: a Manager may approve exactly 50,000.00.
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

    /// <summary>Throws <see cref="ApprovalNotAllowedException"/> with the reason when the approval is not allowed.</summary>
    public void EnsureCanApprove(Invoice invoice, string approver, string role)
    {
        if (!LimitsByRole.TryGetValue(role, out var limit))
        {
            throw new ApprovalNotAllowedException(
                $"The {role} role cannot approve invoices. Approvals need a Manager (up to {Money(ManagerLimit)}) or a Director (up to {Money(DirectorLimit)}).");
        }

        if (invoice.Amount > DirectorLimit)
        {
            throw new ApprovalNotAllowedException(
                $"Invoice {invoice.Id} is {Money(invoice.Amount)}, over the {Money(DirectorLimit)} single-approver limit; it needs two approvers.");
        }

        if (invoice.Amount > limit)
        {
            throw new ApprovalNotAllowedException(
                $"{role} approval limit is {Money(limit)}; invoice {invoice.Id} is {Money(invoice.Amount)} and needs a Director.");
        }
    }

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
