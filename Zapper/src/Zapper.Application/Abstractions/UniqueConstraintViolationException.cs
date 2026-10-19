namespace Zapper.Application.Abstractions;

public sealed class UniqueConstraintViolationException(Exception innerException)
    : Exception("A record with the same unique identifier already exists.", innerException);