namespace Zapper.Application.Payments;

public sealed class PaymentApplicationException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}