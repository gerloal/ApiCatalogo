using System.Collections.Generic;
using Iop.Api.Util;

namespace Iop.Api
{
    public class IopRequest
    {
        private string? apiName;
        private IopDictionary? apiParams;
        private IDictionary<string, FileItem>? fileParams;
        private IopDictionary? headerParams;
        private string httpMethod = Constants.METHOD_POST;

        public IopRequest() { }

        public IopRequest(string apiName)
        {
            this.apiName = apiName;
        }

        public void AddApiParameter(string key, string value)
        {
            apiParams ??= new IopDictionary();
            apiParams.Add(key, value);
        }

        public void AddFileParameter(string key, FileItem file)
        {
            fileParams ??= new Dictionary<string, FileItem>();
            fileParams.Add(key, file);
        }

        public void AddHeaderParameter(string key, string value)
        {
            headerParams ??= new IopDictionary();
            headerParams.Add(key, value);
        }

        public string? GetApiName() => apiName;
        public void SetApiName(string apiName) => this.apiName = apiName;
        public string GetHttpMethod() => httpMethod;
        public void SetHttpMethod(string httpMethod) => this.httpMethod = httpMethod;

        public IDictionary<string, string> GetParameters()
        {
            apiParams ??= new IopDictionary();
            return apiParams;
        }

        public IDictionary<string, FileItem>? GetFileParameters() => fileParams;
        public IDictionary<string, string>? GetHeaderParameters() => headerParams;
    }
}
