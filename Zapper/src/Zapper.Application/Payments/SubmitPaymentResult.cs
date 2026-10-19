namespace Zapper.Application.Payments;

public sealed record SubmitPaymentResult(PaymentStatusResult Payment, bool IsReplay, int? UpstreamStatusCode = null, TimeSpan? UpstreamRetryAfter = null);