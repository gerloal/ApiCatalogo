using System.IO;

namespace Iop.Api.Util
{
    public class FileItem
    {
        private string fileName;
        private string mimeType;
        private byte[]? content;
        private string? filePath;

        public FileItem(string fileName, byte[] content)
        {
            this.fileName = fileName;
            this.content = content;
            this.mimeType = Constants.CTYPE_DEFAULT;
        }

        public FileItem(string fileName, string mimeType, byte[] content)
        {
            this.fileName = fileName;
            this.mimeType = mimeType;
            this.content = content;
        }

        public FileItem(string filePath)
        {
            this.filePath = filePath;
            this.fileName = Path.GetFileName(filePath);
            this.mimeType = Constants.CTYPE_DEFAULT;
        }

        public string GetFileName() => fileName;
        public string GetMimeType() => mimeType;

        public bool IsValid() => content != null || (filePath != null && File.Exists(filePath));

        public void Write(Stream stream)
        {
            if (content != null)
            {
                stream.Write(content, 0, content.Length);
            }
            else if (filePath != null)
            {
                using var fs = File.OpenRead(filePath);
                fs.CopyTo(stream);
            }
        }
    }
}
