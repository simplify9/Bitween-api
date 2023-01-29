namespace SW.Infolink;

public interface IPropertyMatchSpecification
{
    bool IsMatch(IPropertyReader reader);

    string Name { get; }
}