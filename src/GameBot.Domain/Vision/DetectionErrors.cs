using System;

namespace GameBot.Domain.Vision
{
    public class InvalidReferenceImageException : Exception
    {
        public InvalidReferenceImageException() { }
        public InvalidReferenceImageException(string message) : base(message) { }
        public InvalidReferenceImageException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class DetectionTimeoutException : Exception
    {
        public DetectionTimeoutException() { }
        public DetectionTimeoutException(string message) : base(message) { }
        public DetectionTimeoutException(string message, Exception innerException) : base(message, innerException) { }
    }
}
