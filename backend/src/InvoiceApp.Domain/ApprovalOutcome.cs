namespace InvoiceApp.Domain;

/// <summary>What an allowed approval does to the invoice.</summary>
public enum ApprovalOutcome
{
    /// <summary>The approval completes the invoice: it becomes Approved.</summary>
    ApproveFinal,

    /// <summary>The first of two required approvals: recorded, the invoice stays PendingApproval.</summary>
    RecordFirstApproval
}
