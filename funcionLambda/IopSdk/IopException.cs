using System;

namespace Iop.Api
{
    public class IopException : Exception
    {
        public IopException(string message) : base(message) { }
        public IopException(string message, Exception innerException) : base(message, innerException) { }
    }
}
