namespace SW.Infolink.Model;

public interface IPropertyMatchSpecification
{
    bool IsMatch(IExchangePayloadReader reader);

    string Name { get; }
}