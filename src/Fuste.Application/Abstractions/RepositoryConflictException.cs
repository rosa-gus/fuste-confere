namespace Fuste.Application.Abstractions;

/// <summary>A persistence constraint rejected a conflicting record, including concurrent writes.</summary>
public sealed class RepositoryConflictException(string message, Exception innerException) : Exception(message, innerException);
