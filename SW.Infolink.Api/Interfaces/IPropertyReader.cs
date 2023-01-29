namespace SW.Infolink;

public interface IPropertyReader
{
    bool TryGetValue(string path, out string value);
}