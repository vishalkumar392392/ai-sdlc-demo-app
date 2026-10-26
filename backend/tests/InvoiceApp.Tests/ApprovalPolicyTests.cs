using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalPolicyTests
{
    private readonly ApprovalPolicy _policy = new();

    private static Invoice PendingInvoice(decimal amount) =>
        new() { Id = "INV-1", CustomerName = "Test School", Amount = amount, Status = InvoiceStatus.PendingApproval };

    private static Invoice PendingInvoiceApprovedBy(decimal amount, string firstApprover, string firstRole)
    {
        var invoice = PendingInvoice(amount);
        invoice.Approvals.Add(new ApprovalRecord(firstApprover, firstRole, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
        return invoice;
    }

    private ApprovalNotAllowedException Refusal(decimal amount, string approver, string role) =>
        Refusal(PendingInvoice(amount), approver, role);

    private ApprovalNotAllowedException Refusal(Invoice invoice, string approver, string role) =>
        Assert.Throws<ApprovalNotAllowedException>(() => _policy.EnsureCanApprove(invoice, approver, role));

    [Theory]
    [InlineData("0.01")]
    [InlineData("49999.99")]
    [InlineData("50000.00")]
    public void Manager_MayApproveUpToAndIncludingTheLimit(string amount)
    {
        _policy.EnsureCanApprove(PendingInvoice(decimal.Parse(amount)), "demo.manager", ApprovalRoles.Manager);
    }

    [Theory]
    [InlineData("50000.01")]
    [InlineData("500000.00")]
    public void Manager_IsRefusedAboveTheLimitWithAReasonNamingRoleAndLimit(string amount)
    {
        var error = Refusal(decimal.Parse(amount), "demo.manager", ApprovalRoles.Manager);

        Assert.Contains("Manager", error.Message);
        Assert.Contains("50,000.00", error.Message);
        Assert.Contains("Director", error.Message);
    }

    [Theory]
    [InlineData("50000.01")]
    [InlineData("499999.99")]
    [InlineData("500000.00")]
    public void Director_MayApproveUpToAndIncludingTheLimit(string amount)
    {
        _policy.EnsureCanApprove(PendingInvoice(decimal.Parse(amount)), "demo.director", ApprovalRoles.Director);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Approver")]
    [InlineData("manager")]
    [InlineData("")]
    public void RoleWithoutALimit_IsRefusedWithAReasonNamingTheRole(string role)
    {
        var error = Refusal(100m, "someone", role);

        Assert.Contains($"The {role} role cannot approve", error.Message);
    }

    [Theory]
    [InlineData("Manager", "50000.00")]
    [InlineData("Director", "50000.01")]
    [InlineData("Director", "500000.00")]
    public void SingleApproval_UpToTheDirectorLimit_IsFinal(string role, string amount)
    {
        var outcome = _policy.EnsureCanApprove(PendingInvoice(decimal.Parse(amount)), "someone", role);

        Assert.Equal(ApprovalOutcome.ApproveFinal, outcome);
    }

    [Theory]
    [InlineData("Manager", "500000.01")]
    [InlineData("Manager", "750000.00")]
    [InlineData("Director", "500000.01")]
    [InlineData("Director", "750000.00")]
    public void FirstApproval_AboveTheDirectorLimit_IsRecordedNotFinal(string role, string amount)
    {
        var outcome = _policy.EnsureCanApprove(PendingInvoice(decimal.Parse(amount)), "someone", role);

        Assert.Equal(ApprovalOutcome.RecordFirstApproval, outcome);
    }

    [Theory]
    [InlineData("demo.manager", "Manager")]
    [InlineData("demo.director", "Director")]
    public void SecondApproval_ByADifferentDirector_IsFinal(string firstApprover, string firstRole)
    {
        var invoice = PendingInvoiceApprovedBy(750_000m, firstApprover, firstRole);

        var outcome = _policy.EnsureCanApprove(invoice, "demo.director2", ApprovalRoles.Director);

        Assert.Equal(ApprovalOutcome.ApproveFinal, outcome);
        Assert.Single(invoice.Approvals);
    }

    [Fact]
    public void SecondApproval_ByAManager_IsRefusedWithAReasonNamingRoleAndDirector()
    {
        var invoice = PendingInvoiceApprovedBy(750_000m, "demo.director", ApprovalRoles.Director);

        var error = Refusal(invoice, "other.manager", ApprovalRoles.Manager);

        Assert.Contains("Manager", error.Message);
        Assert.Contains("500,000.00", error.Message);
        Assert.Contains("second approval must come from a Director", error.Message);
    }

    [Theory]
    [InlineData("demo.director", "demo.director")]
    [InlineData("demo.director", " Demo.Director ")]
    [InlineData(" Demo.Director ", "demo.director")]
    [InlineData("demo.director", "DEMO.DIRECTOR")]
    public void SecondApproval_BySameDirector_IsRefusedIgnoringCaseAndWhitespace(string firstApprover, string secondApprover)
    {
        var invoice = PendingInvoiceApprovedBy(750_000m, firstApprover, ApprovalRoles.Director);

        var error = Refusal(invoice, secondApprover, ApprovalRoles.Director);

        Assert.Contains("already approved", error.Message);
        Assert.Contains("different Director", error.Message);
    }

    [Fact]
    public void SecondApproval_JustOverTheDirectorLimit_BySameDirector_IsRefused()
    {
        var invoice = PendingInvoiceApprovedBy(500_000.01m, "demo.director", ApprovalRoles.Director);

        Refusal(invoice, "demo.director", ApprovalRoles.Director);
    }

    [Fact]
    public void ApprovalWithinTheLimit_BySomeoneWhoAlreadyApproved_IsRefused()
    {
        var invoice = PendingInvoiceApprovedBy(10_000m, "demo.manager", ApprovalRoles.Manager);

        var error = Refusal(invoice, " Demo.Manager ", ApprovalRoles.Manager);

        Assert.Contains("already approved", error.Message);
    }

    [Fact]
    public void ApprovalWithinTheLimit_BySomeoneElse_IsFinalEvenWithARecordedApproval()
    {
        var invoice = PendingInvoiceApprovedBy(10_000m, "demo.director", ApprovalRoles.Director);

        var outcome = _policy.EnsureCanApprove(invoice, "demo.manager", ApprovalRoles.Manager);

        Assert.Equal(ApprovalOutcome.ApproveFinal, outcome);
    }
}
