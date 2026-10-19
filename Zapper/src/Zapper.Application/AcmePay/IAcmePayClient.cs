namespace Zapper.Application.AcmePay;

public interface IAcmePayClient
{
    Task<AcmePaymentResponseEnvelope> SubmitPaymentAsync(
        AcmePaymentRequestEnvelope request,
        CancellationToken cancellationToken);

    Task<AcmePaymentResponseEnvelope?> QueryTransactionAsync(
        string merchantId,
        string? transactionId,
        string? orderNumber,
        CancellationToken cancellationToken);
}