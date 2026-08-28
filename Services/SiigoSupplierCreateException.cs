namespace CotizadorInterno.Web.Services;

public sealed class SiigoSupplierCreateException : InvalidOperationException
{
    public SiigoSupplierCreateException(
        string message,
        string payloadJson,
        Exception innerException,
        bool isAmbiguous = false)
        : base(message, innerException)
    {
        PayloadJson = payloadJson;
        IsAmbiguous = isAmbiguous;
    }

    public string PayloadJson { get; }

    public bool IsAmbiguous { get; }
}
