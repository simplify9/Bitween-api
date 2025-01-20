namespace SW.Bitween;

public interface IExchangePayloadReader
{
    bool TryGetValue(string path, out string value);
    bool CanGetValues();
}