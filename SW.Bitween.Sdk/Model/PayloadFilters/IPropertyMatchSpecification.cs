namespace SW.Bitween.Model;

public interface IPropertyMatchSpecification
{
    bool IsMatch(IExchangePayloadReader reader);

    string Name { get; }
}