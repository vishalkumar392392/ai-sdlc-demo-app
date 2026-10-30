namespace InvoiceApp.Domain;

/// <summary>
/// Moves an invoice through its lifecycle: Draft -> PendingApproval -> Approved or Rejected.
/// Approvals are limited by the approver's role (see <see cref="ApprovalPolicy"/>); invoices over
/// 500,000 stay pending after the first of two approvals.
/// </summary>
public class ApprovalService
{
    /// <summary>
    /// Serialises every decision: find, check and write must be one step, or two requests can both
    /// pass the checks (two "first" approvals, or an approve racing a decline). Static because the
    /// service is created per request. A database-backed repository would use optimistic concurrency instead.
    /// </summary>
    private static readonly object DecisionLock = new();

    private readonly IInvoiceRepository _repository;
    private readonly ApprovalPolicy _policy;

    public ApprovalService(IInvoiceRepository repository, ApprovalPolicy policy)
    {
        _repository = repository;
        _policy = policy;
    }

    public Invoice Submit(string invoiceId)
    {
        lock (DecisionLock) return SubmitLocked(invoiceId);
    }

    private Invoice SubmitLocked(string invoiceId)
    {
        var invoice = Find(invoiceId);

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Invoice {invoiceId} is {invoice.Status}; only drafts can be submitted.");
        }

        invoice.Status = InvoiceStatus.PendingApproval;
        _repository.Update(invoice);
        return invoice;
    }

    public Invoice Approve(string invoiceId, string approver, string role)
    {
        lock (DecisionLock) return ApproveLocked(invoiceId, approver, role);
    }

    private Invoice ApproveLocked(string invoiceId, string approver, string role)
    {
        var invoice = Find(invoiceId);
        EnsurePending(invoice);

        // Every check runs before the invoice is touched: a refused attempt must change nothing.
        var outcome = _policy.EnsureCanApprove(invoice, approver, role);

        var now = DateTime.UtcNow;
        invoice.Approvals.Add(new ApprovalRecord(approver, role, now));

        if (outcome == ApprovalOutcome.ApproveFinal)
        {
            invoice.Status = InvoiceStatus.Approved;
            invoice.DecidedBy = approver;
            invoice.DecidedAt = now;
        }

        _repository.Update(invoice);
        return invoice;
    }

    public Invoice Reject(string invoiceId, string approver, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A reason is required to reject an invoice.", nameof(reason));
        }

        lock (DecisionLock) return RejectLocked(invoiceId, approver, reason);
    }

    private Invoice RejectLocked(string invoiceId, string approver, string reason)
    {
        var invoice = Find(invoiceId);
        EnsurePending(invoice);

        invoice.Status = InvoiceStatus.Rejected;
        invoice.DecidedBy = approver;
        invoice.DecidedAt = DateTime.UtcNow;
        invoice.DecisionNote = reason;
        _repository.Update(invoice);
        return invoice;
    }

    private Invoice Find(string invoiceId) =>
        _repository.GetById(invoiceId)
        ?? throw new KeyNotFoundException($"Invoice {invoiceId} was not found.");

    private static void EnsurePending(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Invoice {invoice.Id} is {invoice.Status}; only pending invoices can be decided.");
        }
    }
}
