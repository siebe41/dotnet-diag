namespace DotnetDiag.Core.Parsing;

public sealed class LogParseException(string message, Exception? innerException = null)
    : Exception(message, innerException);
