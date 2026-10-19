namespace Zapper.Application.Payments;

public interface IPaymentService
{
    Task<SubmitPaymentResult> SubmitAsync(SubmitPaymentCommand command, CancellationToken cancellationToken);

    Task<PaymentStatusResult?> GetByAppPaymentIdAsync(Guid appPaymentId, CancellationToken cancellationToken);
}