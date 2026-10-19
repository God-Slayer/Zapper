namespace Zapper.Application.AcmePay;

public sealed class AcmePayException(
    string code,
    string message,
    int statusCode,
    string? source = null,
    string? clientMessage = null,
    string? otherTransactionId = null,
    TimeSpan? retryAfter = null,
    string? rawPayload = null,
    string? responseMessageId = null,
    string? transactionStatus = null,
    string? reconId = null,
    string? authCode = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public string? ResponseSource { get; } = source;
    public string? ClientMessage { get; } = clientMessage;
    public string? OtherTransactionId { get; } = otherTransactionId;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string? RawPayload { get; } = rawPayload;
    public string? ResponseMessageId { get; } = responseMessageId;
    public string? TransactionStatus { get; } = transactionStatus;
    public string? ReconId { get; } = reconId;
    public string? AuthCode { get; } = authCode;
}