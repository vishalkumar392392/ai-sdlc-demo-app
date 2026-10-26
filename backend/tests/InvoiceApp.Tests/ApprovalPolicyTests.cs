using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class ApprovalPolicyTests
{
    private readonly ApprovalPolicy _policy = new();

    private static Invoice PendingInvoice(decimal amount) =>
        new() { Id = "INV-1", CustomerName = "Test School", Amount = amount, Status = InvoiceStatus.PendingApproval };

    private ApprovalNotAllowedException Refusal(decimal amount, string approver, string role) =>
        Assert.Throws<ApprovalNotAllowedException>(() => _policy.EnsureCanApprove(PendingInvoice(amount), approver, role));

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
}
