using System.Threading.Tasks;
using SW.Bitween.Domain;

namespace SW.Bitween;

public interface IInfolinkCache
{
    public Task<Subscription[]> ListSubscriptionsByDocumentAsync(int documentId);
    public Task<Notifier[]> ListNotifiersAsync();

    public Task<Subscription> SubscriptionByIdAsync(int subscriptionId);
    public Task<Document> DocumentByIdAsync(int documentId);
    public Task<Document> DocumentByNameAsync(string documentName);


    void Revoke();
    void BroadcastRevoke();

    Task<WorkGroup[]> ListWorkGroupsAsync();
    Task<WorkGroup> WorkGroupByIdAsync(int workGroupId);
}