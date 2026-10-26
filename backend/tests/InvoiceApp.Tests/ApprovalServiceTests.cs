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
        (invoice.Status, invoice.DecidedBy, invoice.DecidedAt, invoice.DecisionNote, invoice.Amount,
            string.Join(" | ", invoice.Approvals));

    private static readonly ApprovalRecord FirstDirectorApproval =
        new("demo.director", Director, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

    private static readonly ApprovalRecord FirstManagerApproval =
        new("demo.manager", Manager, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

    private static Invoice PendingWithApproval(decimal amount, ApprovalRecord firstApproval)
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, amount);
        invoice.Approvals.Add(firstApproval);
        return invoice;
    }

    /// <summary>A refused second approval: still pending, no decision, only the original approval, nothing saved.</summary>
    private static void AssertUnchangedWithOnlyTheFirstApproval(
        Invoice invoice, object before, ApprovalRecord firstApproval, FakeInvoiceRepository repository)
    {
        Assert.Equal(before, Snapshot(invoice));
        Assert.Equal(InvoiceStatus.PendingApproval, invoice.Status);
        Assert.Null(invoice.DecidedBy);
        Assert.Null(invoice.DecidedAt);
        Assert.Null(invoice.DecisionNote);
        Assert.Equal(new[] { firstApproval }, invoice.Approvals);
        Assert.Equal(0, repository.UpdateCount);
    }

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

    [Fact]
    public void Approve_DirectorAtTheDirectorLimitIsApprovedInOneStepWithOneRecord()
    {
        var repository = new FakeInvoiceRepository(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 500_000.00m));

        var result = ServiceFor(repository).Approve("INV-1", "demo.director", Director);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.director", result.DecidedBy);
        var record = Assert.Single(result.Approvals);
        Assert.Equal(("demo.director", Director), (record.Approver, record.Role));
        Assert.Equal(result.DecidedAt, record.ApprovedAt);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Theory]
    [InlineData("500000.01", "demo.director", "Director")]
    [InlineData("750000.00", "demo.director", "Director")]
    [InlineData("750000.00", "demo.manager", "Manager")]
    public void Approve_FirstApprovalAboveTheDirectorLimitIsRecordedAndStaysPending(string amount, string approver, string role)
    {
        var repository = new FakeInvoiceRepository(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, decimal.Parse(amount)));
        var startedAt = DateTime.UtcNow;

        var result = ServiceFor(repository).Approve("INV-1", approver, role);

        Assert.Equal(InvoiceStatus.PendingApproval, result.Status);
        Assert.Null(result.DecidedBy);
        Assert.Null(result.DecidedAt);
        Assert.Null(result.DecisionNote);
        var record = Assert.Single(result.Approvals);
        Assert.Equal(approver, record.Approver);
        Assert.Equal(role, record.Role);
        Assert.InRange(record.ApprovedAt, startedAt, DateTime.UtcNow);
        Assert.Equal(1, repository.UpdateCount);
    }

    [Theory]
    [InlineData("500000.01")]
    [InlineData("750000.00")]
    public void Approve_SecondDirectorApprovalCompletesTheInvoice(string amount)
    {
        var invoice = PendingWithApproval(decimal.Parse(amount), FirstManagerApproval);
        var repository = new FakeInvoiceRepository(invoice);

        var result = ServiceFor(repository).Approve("INV-1", "demo.director2", Director);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.director2", result.DecidedBy);
        Assert.NotNull(result.DecidedAt);
        Assert.Equal(2, result.Approvals.Count);
        Assert.Equal(FirstManagerApproval, result.Approvals[0]);
        Assert.Equal(("demo.director2", Director, result.DecidedAt!.Value),
            (result.Approvals[1].Approver, result.Approvals[1].Role, result.Approvals[1].ApprovedAt));
        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Approve_TwoStepsEndToEndThroughTheService()
    {
        var repository = new FakeInvoiceRepository(InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 750_000m));
        var service = ServiceFor(repository);

        service.Approve("INV-1", "demo.manager", Manager);
        var result = service.Approve("INV-1", "demo.director2", Director);

        Assert.Equal(InvoiceStatus.Approved, result.Status);
        Assert.Equal("demo.director2", result.DecidedBy);
        Assert.Equal(new[] { "demo.manager", "demo.director2" }, result.Approvals.Select(record => record.Approver));
        Assert.Equal(2, repository.UpdateCount);
    }

    [Theory]
    [InlineData("demo.director")]
    [InlineData(" Demo.Director ")]
    [InlineData("DEMO.DIRECTOR")]
    public void Approve_SameDirectorTwiceIsRefusedAndNothingAboutTheInvoiceChanges(string secondApprover)
    {
        var invoice = PendingWithApproval(750_000m, FirstDirectorApproval);
        var repository = new FakeInvoiceRepository(invoice);
        var before = Snapshot(invoice);

        var error = Assert.Throws<ApprovalNotAllowedException>(
            () => ServiceFor(repository).Approve("INV-1", secondApprover, Director));

        Assert.Contains("different Director", error.Message);
        AssertUnchangedWithOnlyTheFirstApproval(invoice, before, FirstDirectorApproval, repository);
    }

    [Fact]
    public void Approve_SameDirectorTwiceJustOverTheDirectorLimitIsRefusedAndKeepsTheFirstApproval()
    {
        var invoice = PendingWithApproval(500_000.01m, FirstDirectorApproval);
        var repository = new FakeInvoiceRepository(invoice);
        var before = Snapshot(invoice);

        Assert.Throws<ApprovalNotAllowedException>(
            () => ServiceFor(repository).Approve("INV-1", " Demo.Director ", Director));

        AssertUnchangedWithOnlyTheFirstApproval(invoice, before, FirstDirectorApproval, repository);
    }

    [Fact]
    public void Approve_ManagerAsSecondApproverIsRefusedAndNothingAboutTheInvoiceChanges()
    {
        var invoice = PendingWithApproval(750_000m, FirstDirectorApproval);
        var repository = new FakeInvoiceRepository(invoice);
        var before = Snapshot(invoice);

        var error = Assert.Throws<ApprovalNotAllowedException>(
            () => ServiceFor(repository).Approve("INV-1", "demo.manager", Manager));

        Assert.Contains("Manager", error.Message);
        Assert.Contains("must come from a Director", error.Message);
        AssertUnchangedWithOnlyTheFirstApproval(invoice, before, FirstDirectorApproval, repository);
    }

    [Fact]
    public void Approve_ManagerOverLimitIsRefusedAndRecordsNoApproval()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.PendingApproval, 50_000.01m);
        var repository = new FakeInvoiceRepository(invoice);

        Assert.Throws<ApprovalNotAllowedException>(
            () => ServiceFor(repository).Approve("INV-1", "demo.manager", Manager));

        Assert.Empty(invoice.Approvals);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public void Submit_IsSaved()
    {
        var repository = new FakeInvoiceRepository(InvoiceWith("INV-1", InvoiceStatus.Draft));

        ServiceFor(repository).Submit("INV-1");

        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Reject_IsSaved()
    {
        var repository = new FakeInvoiceRepository(InvoiceWith("INV-1", InvoiceStatus.PendingApproval));

        ServiceFor(repository).Reject("INV-1", "demo.manager", "Duplicate invoice");

        Assert.Equal(1, repository.UpdateCount);
    }

    [Fact]
    public void Reject_RefusesInvoiceThatIsNotPendingAndChangesNothing()
    {
        var invoice = InvoiceWith("INV-1", InvoiceStatus.Approved);
        var repository = new FakeInvoiceRepository(invoice);
        var before = Snapshot(invoice);

        var error = Assert.Throws<InvalidOperationException>(
            () => ServiceFor(repository).Reject("INV-1", "demo.manager", "Too late"));

        Assert.Contains("only pending invoices can be decided", error.Message);
        Assert.Equal(before, Snapshot(invoice));
        Assert.Equal(0, repository.UpdateCount);
    }
}
