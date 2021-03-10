using System.Threading.Tasks;
using System.Windows.Input;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Xchanges
{
    public class Create: ICommandHandler<string, CreateXchange>
    {
        public Task<object> Handle(string key, CreateXchange request)
        {
            throw new System.NotImplementedException();
        }
    }
}