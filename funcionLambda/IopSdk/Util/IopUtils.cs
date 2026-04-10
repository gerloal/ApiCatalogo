using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Iop.Api.Util
{
    public abstract class IopUtils
    {
        private static string? intranetIp;

        public static string SignRequest(string apiName, IDictionary<string, string> parameters, string appSecret, string signMethod)
        {
            return SignRequest(apiName, parameters, null, appSecret, signMethod);
        }

        public static string SignRequest(string apiName, IDictionary<string, string> parameters, string? body, string appSecret, string signMethod)
        {
            IDictionary<string, string> sortedParams = new SortedDictionary<string, string>(parameters, StringComparer.Ordinal);

            var query = new StringBuilder();
            query.Append(apiName);

            foreach (KeyValuePair<string, string> kv in sortedParams)
            {
                if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                {
                    query.Append(kv.Key).Append(kv.Value);
                }
            }

            if (!string.IsNullOrEmpty(body))
            {
                query.Append(body);
            }

            byte[] bytes;
            if (signMethod.Equals(Constants.SIGN_METHOD_SHA256))
            {
                using var sha256 = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
                bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(query.ToString()));
            }
            else
            {
                throw new Exception("Invalid Sign Method");
            }

            var result = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                result.Append(bytes[i].ToString("X2"));
            }
            return result.ToString();
        }

        public static string GetIntranetIp()
        {
            if (intranetIp == null)
            {
                NetworkInterface[] nis = NetworkInterface.GetAllNetworkInterfaces();
                foreach (NetworkInterface ni in nis)
                {
                    if (OperationalStatus.Up == ni.OperationalStatus &&
                        (NetworkInterfaceType.Ethernet == ni.NetworkInterfaceType || NetworkInterfaceType.Wireless80211 == ni.NetworkInterfaceType))
                    {
                        foreach (UnicastIPAddressInformation info in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (AddressFamily.InterNetwork == info.Address.AddressFamily)
                            {
                                intranetIp = info.Address.ToString();
                                break;
                            }
                        }
                        if (intranetIp != null) break;
                    }
                }
            }
            return intranetIp ?? "127.0.0.1";
        }
    }
}
