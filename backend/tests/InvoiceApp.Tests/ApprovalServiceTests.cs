using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalServiceTests
{
    private const string Manager = ApprovalRoles.Manager;
    private const string Director = ApprovalRoles.Director;

    private static Invoice InvoiceWith(string id, InvoiceStatus status, decimal amount = 100m) =>
        new() { Id = id, CustomerName = "Test School", Amount = amount, Status = status };

    private static ApprovalService ServiceFor(params Invoice[] invoices) =>
        ServiceFor(new FakeInvoiceRepository(invoices));

    private static ApprovalService ServiceFor(FakeInvoiceRepository repository) =>
        new(repository, new ApprovalPolicy());

    /// <summary>Every field an approval could touch, so "nothing changed" is one equality check.</summary>
    private static object Snapshot(Invoice invoice) =>
        (invoice.Status, invoice.DecidedBy, invoice.DecidedAt, invoice.DecisionNote, invoice.Amount);

    [Fact]
    public void Submit_MovesDraftToPendingApproval()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.Draft);

        var result = ServiceFor(invoice).Submit("INV-1");

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
    }

    [Fact]
    public void Submit_RejectsInvoiceThatIsNotADraft()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Approved));

        Assert.Throws<InvalidOperationException>(() => service.Submit("INV-1"));
    }

    [Fact]
    public void Approve_SetsStatusApproverAndTimestamp()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var result = service.Approve("INV-1", "demo.manager", Manager);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.manager", result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
    }

    [Fact]
    public void Approve_RejectsInvoiceThatIsStillADraft()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.Draft));

        Assert.Throws<InvalidOperationException>(() => service.Approve("INV-1", "demo.manager", Manager));
    }

    [Fact]
    public void Approve_ThrowsWhenInvoiceDoesNotExist()
    {
        var service = ServiceFor();

        Assert.Throws<KeyNotFoundException>(() => service.Approve("INV-404", "demo.manager", Manager));
    }

    [Fact]
    public void Reject_RecordsReasonAndApprover()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        var result = service.Reject("INV-1", "demo.approver", "Duplicate invoice");

        Assert.Equal(InvoiceStatus.Rejected, result.Status);
        Assert.Equal("Duplicate invoice", result.DecisionNote);
    }

    [Fact]
    public void Reject_RequiresAReason()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        Assert.Throws<ArgumentException>(() => service.Reject("INV-1", "demo.approver", "  "));
    }

    [Fact]
    public void Approve_ManagerApprovesInvoiceExactlyAtTheManagerLimit()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 50_000.00m));

        var result = service.Approve("INV-1", "demo.manager", Manager);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.manager", result.DecidedBy);
    }

    [Fact]
    public void Approve_DirectorApprovesInvoiceExactlyAtTheDirectorLimit()
    {
        var service = ServiceFor(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 500_000.00m));

        var result = service.Approve("INV-1", "demo.director", Director);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.director", result.DecidedBy);
    }

    [Fact]
    public void Approve_ManagerOverLimitIsRefusedAndNothingAboutTheInvoiceChanges()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 50_000.01m);
        var repository = new FakeInvoiceRepository(invoice);
        var before = Snapshot(invoice);

        var error = Assert.Throws<ApprovalNotAllowedException>(
            () => ServiceFor(repository).Approve("INV-1", "demo.manager", Manager));

        Assert.Contains("Manager", error.Message);
        Assert.Contains("50,000.00", error.Message);
        Assert.Equal(before, Snapshot(invoice));
        Assert.Equal(InvoiceStatus.PendingApproval, invoice.Status);
        Assert.Null(invoice.DecidedBy);
        Assert.Null(invoice.DecidedAt);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public void Approve_RoleWithoutALimitIsRefusedAndNothingAboutTheInvoiceChanges()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 100m);
        var repository = new FakeInvoiceRepository(invoice);
        var before = Snapshot(invoice);

        var error = Assert.Throws<ApprovalNotAllowedException>(
            () => ServiceFor(repository).Approve("INV-1", "demo.admin", ApprovalRoles.Admin));

        Assert.Contains("Admin", error.Message);
        Assert.Equal(before, Snapshot(invoice));
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public void Approve_SuccessfulApprovalIsSaved()
    {
        var repository = new FakeInvoiceRepository(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 1_200m));

        ServiceFor(repository).Approve("INV-1", "demo.manager", Manager);

        Assert.Equal(1, repository.UpdateCount);
    }
}
