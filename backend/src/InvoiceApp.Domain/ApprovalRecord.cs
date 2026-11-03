namespace InvoiceApp.Domain;

/// <summary>One approval given on an invoice: who approved, in which role, and when.</summary>
public record ApprovalRecord(string Approver, string Role, DateTime ApprovedAt);
