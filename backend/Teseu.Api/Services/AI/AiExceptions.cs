namespace Teseu.Api.Services.AI;

public class AiServiceException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class AiUnavailableException(string message, Exception? innerException = null) : AiServiceException(message, innerException);

public sealed class AiTimeoutException(string message, Exception? innerException = null) : AiServiceException(message, innerException);

public sealed class AiInvalidResponseException(string message, Exception? innerException = null) : AiServiceException(message, innerException);
