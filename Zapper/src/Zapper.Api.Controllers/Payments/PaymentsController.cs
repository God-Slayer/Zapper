using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Zapper.Application.Payments;

namespace Zapper.Api.Controllers.Payments;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PaymentStatusResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<PaymentStatusResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<PaymentStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] SubmitPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await paymentService.SubmitAsync(new SubmitPaymentCommand(
                request.CustomerId,
                request.SubMerchantId,
                request.Amount,
                request.CurrencyCode,
                request.AppPaymentId), cancellationToken);
            var response = ToResponse(result.Payment);

            if (result.UpstreamStatusCode is { } upstreamStatus)
            {
                if (result.UpstreamRetryAfter is { } retryAfter)
                    Response.Headers["Retry-After"] = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(upstreamStatus, new
                {
                    code = result.Payment.AcmeResponseCode ?? "ACME_SUBMISSION_FAILED",
                    message = result.Payment.AcmeResponseDescription ?? "Acme could not process the payment.",
                    clientMessage = result.Payment.AcmeClientMessage,
                    otherTransactionId = result.Payment.AcmeOtherTransactionId,
                    payment = response
                });
            }
            if (result.IsReplay) return Ok(response);
            if (result.Payment.Status is Zapper.Domain.Enums.PaymentStatus.Processing or Zapper.Domain.Enums.PaymentStatus.OutcomeUnknown)
                return AcceptedAtAction(nameof(GetByAppPaymentId), new { appPaymentId = request.AppPaymentId }, response);
            return CreatedAtAction(nameof(GetByAppPaymentId), new { appPaymentId = request.AppPaymentId }, response);
        }
        catch (PaymentApplicationException ex)
        {
            return StatusCode(ex.StatusCode, new { code = ex.Code, message = ex.Message });
        }
    }

    [HttpGet("{appPaymentId:guid}")]
    [ProducesResponseType<PaymentStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByAppPaymentId(Guid appPaymentId, CancellationToken cancellationToken)
    {
        var result = await paymentService.GetByAppPaymentIdAsync(appPaymentId, cancellationToken);
        return result is null ? NotFound(new { code = "PAYMENT_NOT_FOUND", message = "Payment was not found." }) : Ok(ToResponse(result));
    }

    private static PaymentStatusResponse ToResponse(PaymentStatusResult result) => new(
        result.PaymentId,
        result.AppPaymentId,
        result.Status.ToString(),
        result.AmountMinorUnits,
        result.CurrencyCode,
        result.AuthorizedAmountMinorUnits,
        result.AcmeResponseCode,
        result.CreatedAt,
        result.UpdatedAt,
        result.AcmeMessageId,
        result.AcmeTransactionStatus,
        result.AcmeResponseSource,
        result.AcmeResponseDescription,
        result.AcmeClientMessage,
        result.AcmeReconId,
        result.AcmeAuthCode,
        result.AcmeOtherTransactionId);
}