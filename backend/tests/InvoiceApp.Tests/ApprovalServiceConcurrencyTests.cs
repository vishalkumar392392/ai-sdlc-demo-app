using InvoiceApp.Domain;
using Xunit;

namespace InvoiceApp.Tests;

/// <summary>
/// Two approvers acting on the same invoice at the same moment (review finding F1).
/// A repository that holds both requests at GetById until both have arrived makes the
/// interleaving likely; with the decision lock in place the second request waits instead.
/// </summary>
public class ApprovalServiceConcurrencyTests
{
    private const int Rounds = 25;

    private sealed class RendezvousRepository : IInvoiceRepository
    {
        private readonly FakeInvoiceRepository _inner;
        private readonly Barrier _barrier = new(2);

        public RendezvousRepository(Invoice invoice) => _inner = new FakeInvoiceRepository(invoice);

        public Invoice? GetById(string id)
        {
            // Wait briefly for the other request; under the lock it never arrives, so time out and carry on.
            _barrier.SignalAndWait(TimeSpan.FromMilliseconds(20));
            return _inner.GetById(id);
        }

        public IReadOnlyList<Invoice> GetAll() => _inner.GetAll();
        public IReadOnlyList<Invoice> GetByIds(IEnumerable<string> ids) => _inner.GetByIds(ids);
        public Invoice Add(Invoice invoice) => _inner.Add(invoice);
        public void Update(Invoice invoice) => _inner.Update(invoice);
        public bool Remove(string id) => _inner.Remove(id);
    }

    private static void RunTogether(Action first, Action second)
    {
        var tasks = new[] { Task.Run(first), Task.Run(second) };
        try { Task.WaitAll(tasks); } catch (AggregateException) { /* refusals are expected outcomes */ }
    }

    [Fact]
    public void TwoDirectorsApprovingAtOnce_EndWithOneFirstAndOneFinalApproval()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var invoice = new Invoice { Id = "INV-1", Amount = 640_000m, Status = InvoiceStatus.PendingApproval };
            var service = new ApprovalService(new RendezvousRepository(invoice), new ApprovalPolicy());

            RunTogether(
                () => service.Approve("INV-1", "demo.director", ApprovalRoles.Director),
                () => service.Approve("INV-1", "demo.director2", ApprovalRoles.Director));

            Assert.Equal(InvoiceStatus.Approved, invoice.Status);
            Assert.Equal(2, invoice.Approvals.Count);
        }
    }

    [Fact]
    public void ApproveAndDeclineAtOnce_LeaveAConsistentDecision()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var invoice = new Invoice { Id = "INV-1", Amount = 10_000m, Status = InvoiceStatus.PendingApproval };
            var service = new ApprovalService(new RendezvousRepository(invoice), new ApprovalPolicy());

            RunTogether(
                () => service.Approve("INV-1", "demo.director", ApprovalRoles.Director),
                () => service.Reject("INV-1", "demo.admin", "Duplicate"));

            if (invoice.Status == InvoiceStatus.Approved)
            {
                Assert.Equal("demo.director", invoice.DecidedBy);
                Assert.Null(invoice.DecisionNote);
            }
            else
            {
                Assert.Equal(InvoiceStatus.Rejected, invoice.Status);
                Assert.Equal("demo.admin", invoice.DecidedBy);
                Assert.Empty(invoice.Approvals);
            }
        }
    }
}
