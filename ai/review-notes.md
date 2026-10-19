# Assignment Review and Correction Notes

This note records material gaps found during the assignment audit, the resulting corrections, and the evidence used to check them. It is a concise engineering review record rather than a full audit transcript.

| Review finding | Correction | Verification |
| --- | --- | --- |
| The FNB `CardAcceptorName` did not include the payment-facilitator name in positions 1–23 as Appendix A requires. | Format the merchant portion as `FacilitatorName*SubMerchantName`, configure the facilitator name, preserve the 40-character layout, and use the first two region characters in positions 37–38. The local `ZAP` prefix puts `*` at position 4. | The descriptor was checked at 40 characters with the expected region and country positions; the automated suite also checks descriptor positions. |
| Payment Advice handling did not fully accept the documented `COMPL` and `PEND` response codes and status values. | Accept the documented callback values; record `PEND` as a processing event and handle `COMPL` as a completed outcome. | Callback checks confirmed HTTP 200 and the expected persisted processing/completed behavior; automated tests cover both codes. |
| When `TransactionDateTime` was absent, receipt time could incorrectly make an older callback appear newer than a known terminal payment outcome. | Retain receipt time for audit while leaving the provider ordering time unknown; do not use receipt time to supersede the known outcome. | A timestamp-less stale failure was delivered and the previously approved payment remained approved; an automated regression test covers this behavior. |
| Concurrent stub requests could race while claiming the same RRN. | Make the RRN ownership check and transaction registration atomic; preserve same-transaction replay and return E09 for a conflicting owner. | Twelve simultaneous requests with one RRN produced one accepted response and eleven HTTP 409/E09 responses. |
| Startup migration and seed behavior needed a clearer Development/Production boundary. | Keep automatic migration and synthetic seed behavior for Development/local Compose; default non-Development startup to migrations disabled and document a controlled, one-off production migration step. Expand stable local demo data to two sub-merchants and approved/declined payments. | Production-mode startup was healthy with migrations disabled. Recreating the Development API left stable seed counts unchanged. |

## Additional verification record

- `dotnet build Zapper/Zapper.sln --no-restore --verbosity minimal` passed with zero warnings and errors.
- `dotnet test Zapper/Zapper.sln --no-restore --verbosity minimal` passed: 20 tests, zero failed, zero skipped.
- `docker compose config --quiet` passed; the API, Acme stub, PostgreSQL, and Seq services reached healthy status after a clean build/start.
- Final manual acceptance covered approval and decline, idempotent replay and conflicting reuse, duplicate callbacks, reversal and partial refund, authenticated merchant query, daily summary, timeout reconciliation with original identifiers, E29 retry, E50 gateway failure, and concurrent RRN collision.
- The user independently ran the project and test suite, inspected the containers and service communication, and checked Seq for visible application logs.

## Review boundary

These notes record corrected gaps and the associated checks. They do not claim that every possible provider response or production condition was exhaustively tested. The Acme `.example` hosts are placeholders, so validation used the local simulator rather than a live provider.
