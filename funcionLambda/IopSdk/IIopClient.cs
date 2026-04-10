using System;

namespace Iop.Api
{
    public interface IIopClient
    {
        IopResponse Execute(IopRequest request);
        IopResponse Execute(IopRequest request, string accessToken);
        IopResponse Execute(IopRequest request, string accessToken, DateTime timestamp);
    }
}
