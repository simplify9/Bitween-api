using System.Threading.Tasks;
using SW.Infolink.Domain;

namespace SW.Infolink;

public interface IInfolinkCache
{
    Task<Subscription[]> ListSubscriptionsByDocumentAsync(int documentId);

    Task<Document> DocumentByIdAsync(int documentId);

    void Revoke();
}