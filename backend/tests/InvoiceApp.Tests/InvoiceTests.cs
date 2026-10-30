using System.Text.Json;
using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

public class InvoiceTests
{
    [Fact]
    public void Approvals_AreEmptyByDefault()
    {
        Assert.Empty(new Invoice().Approvals);
    }

    [Fact]
    public void Approvals_AreSerialisedWithApproverRoleAndTime()
    {
        var invoice = new Invoice { Id = "INV-1" };
        invoice.Approvals.Add(new ApprovalRecord("demo.manager", ApprovalRoles.Manager, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)));

        var json = JsonSerializer.Serialize(invoice, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"approvals\":[{\"approver\":\"demo.manager\",\"role\":\"Manager\",\"approvedAt\":\"2026-01-02T03:04:05Z\"}]", json);
    }
}
