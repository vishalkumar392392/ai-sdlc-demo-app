namespace InvoiceApp.Domain;

/// <summary>
/// An approval attempt broke an approval rule. The message is the reason shown to the user.
/// Thrown before anything about the invoice has changed.
/// </summary>
public class ApprovalNotAllowedException : Exception
{
    public ApprovalNotAllowedException(string reason) : base(reason)
    {
    }
}
