using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using static QuickAPI.QuickAPI.API;

namespace QuickAPI
{
    public class QuickAPI
    {
        /// <summary>
        /// 使用方法构建一个API实例
        /// </summary>
        /// <param name="port">监听端口</param>
        /// <param name="method">当接口被访问时触发的方法</param>
        /// <returns></returns>
        public static API CreateAPI(int port, RequestMgr method)
        {
            return new API(port, method);
        }
        public class API
        {
            private static TcpListener listener = null!;
            public delegate void RequestMgr(Request args);
            private RequestMgr requestMgr;
            public API(int port, RequestMgr method)
            {
                listener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
                listener.Start();
                requestMgr = new RequestMgr(method);
                Task.Run(ListenThread);
            }
            public void ListenThread()
            {
                while (true)
                {
                    try
                    {
                        TcpClient client = listener.AcceptTcpClient();
                        _ = Task.Run(() =>
                        {
                            //获取客户端发送的信息
                            using NetworkStream streamToServer = client.GetStream();
                            //逐字符读取字节流，直到遇到 \r\n\r\n
                            StringBuilder headerBuilder = new();
                            int prevChar = -1;
                            int currChar;
                            while ((currChar = streamToServer.ReadByte()) != -1)
                            {
                                headerBuilder.Append((char)currChar);
                                // 判断是否遇到 \r\n\r\n
                                if (prevChar == '\r' && currChar == '\n')
                                {
                                    prevChar = currChar;
                                    currChar = streamToServer.ReadByte();
                                    headerBuilder.Append((char)currChar);
                                    if (currChar == '\r')
                                    {
                                        prevChar = currChar;
                                        currChar = streamToServer.ReadByte();
                                        headerBuilder.Append((char)currChar);
                                        if (currChar == '\n')
                                        {
                                            break;
                                        }
                                    }
                                }
                                else
                                {
                                    prevChar = currChar;
                                }
                            }
                            // 将读取到的头部数据转换为字符串
                            string headerRaw = headerBuilder.ToString();
                            string[] headerLines = headerRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            // 提取头部字段
                            Dictionary<string, string> Headers = new();
                            for (int i = 1; i < headerLines.Length; i++)
                            {
                                string line = headerLines[i];
                                int separatorIndex = line.IndexOf(':');
                                if (separatorIndex != -1)
                                {
                                    string key = line[..separatorIndex].Trim();
                                    string value = line[(separatorIndex + 1)..].Trim();
                                    Headers[key] = value;
                                }
                            }
                            // 获取正文
                            byte[] buffer;
                            if (Headers.TryGetValue("Content-Length", out string? contentLengthHeaderValue) && int.TryParse(contentLengthHeaderValue, out int contentLength) && contentLength >= 0)
                            {
                                buffer = new byte[int.Parse(Headers["Content-Length"])];
                                var bassetream = streamToServer.Read(buffer, 0, buffer.Length);
                            }
                            else
                            {
                                buffer = Array.Empty<byte>();
                            }
                            //触发委托
                            requestMgr.Invoke(new Request(headerRaw, buffer, client));
                            //关闭连接
                            client.Close();
                            client.Dispose();

                        });
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            /// <summary>
            /// 包含请求的相关信息
            /// </summary>
            public class Request
            {
                public Request(string headerRaw, byte[] bodyRaw, TcpClient client)
                {
                    //重要信息
                    this.HeaderRaw = headerRaw;
                    this.BodyRaw = bodyRaw;
                    this.Client = client;
                    string requestLine = headerRaw.Split('\n')[0];
                    string[] tokens = requestLine.Split(' ');
                    if (tokens.Length <= 1)
                    {
                        //异常情况
                        throw new HttpRequestException($"请求头错误:{headerRaw}");
                    }
                    //请求类型
                    this.RequestMethod = tokens[0];
                    //请求地址
                    this.Url = tokens[1];
                    //请求头的键值对信息
                    this.Headers = new();
                    // 将请求头按行分割

                }
                /// <summary>
                /// 请求头原始数据
                /// </summary>
                public string HeaderRaw { get; set; }
                /// <summary>
                /// 正文原始数据
                /// </summary>
                public byte[] BodyRaw { get; set; }
                /// <summary>
                /// 请求方式
                /// </summary>
                public string RequestMethod { get; }
                /// <summary>
                /// 请求路径
                /// </summary>
                public string Url { get; set; }
                /// <summary>
                /// 请求头中的键值对信息
                /// </summary>
                public Dictionary<string, string> Headers { get; set; }
                /// <summary>
                /// 连接的客户端
                /// </summary>
                public TcpClient Client { get; set; }
                /// <summary>
                /// 从url中获取参数键值
                /// </summary>
                /// <returns></returns>
                public Dictionary<string, string?> GetParametersFromUrl()
                {
                    Dictionary<string, string?> parameters = new();
                    // 正则表达式匹配查询参数
                    MatchCollection matches = Regex.Matches(this.Url, @"[?&]([^&=]+)(?:=([^&]*))?");
                    foreach (Match match in matches.Cast<Match>())
                    {
                        string key = Uri.UnescapeDataString(match.Groups[1].Value);
                        string? value = match.Groups.Count > 2 ? Uri.UnescapeDataString(match.Groups[2].Value) : null;
                        parameters[key] = value;
                    }
                    return parameters;
                }
                /// <summary>
                /// 从正文读取键值对格式的参数
                /// </summary>
                /// <returns></returns>
                public Dictionary<string, string?> GetParametersFromBody()
                {
                    string body = System.Text.Encoding.UTF8.GetString(BodyRaw);
                    Dictionary<string, string?> parameters = new();
                    MatchCollection matches = Regex.Matches(body, @"(?<key>[^&=]+)(?:=(?<value>[^&]*))?");
                    foreach (Match match in matches.Cast<Match>())
                    {
                        string key = Uri.UnescapeDataString(match.Groups["key"].Value);
                        string? value = match.Groups["value"].Success ? Uri.UnescapeDataString(match.Groups["value"].Value) : null;
                        parameters[key] = value;
                    }
                    return parameters;
                }
                /// <summary>
                /// 将正文数据转换为字符串
                /// </summary>
                /// <returns></returns>
                public string GetTextFromBody()
                {
                    return System.Text.Encoding.UTF8.GetString(BodyRaw);
                }
                /// <summary>
                /// 发送文本(json)给客户端
                /// </summary>
                /// <param name="client"></param>
                /// <param name="reture_text"></param>
                public void SendTextToClient(string reture_text)
                {
                    NetworkStream streamToServer = Client.GetStream();
                    StringBuilder response = new();
                    response.Append("HTTP/1.1 200 OK\r\n");
                    response.Append("Content-Type: application/json; charset=utf-8\r\n");
                    response.Append($"Content-Length: {Encoding.UTF8.GetBytes(reture_text).Length}\r\n");
                    response.Append("\r\n");
                    byte[] headerBytes = Encoding.UTF8.GetBytes(response.ToString());
                    streamToServer.Write(headerBytes, 0, headerBytes.Length);
                    byte[] bodyBytes = Encoding.UTF8.GetBytes(reture_text);
                    streamToServer.Write(bodyBytes, 0, bodyBytes.Length);
                }
                /// <summary>
                /// 发送图片给客户端
                /// </summary>
                /// <param name="client"></param>
                /// <param name="reture_text"></param>
                public void SendImageToClient(byte[] imageBytes)
                {
                    NetworkStream streamToServer = Client.GetStream();
                    StringBuilder response = new();
                    response.Append("HTTP/1.1 200 OK\r\n");
                    response.Append($"Content-Type: image/png\r\n");
                    response.Append($"Content-Length: {imageBytes.Length}\r\n");
                    response.Append("\r\n");
                    byte[] headerBytes = Encoding.UTF8.GetBytes(response.ToString());
                    streamToServer.Write(headerBytes, 0, headerBytes.Length);
                    streamToServer.Write(imageBytes, 0, imageBytes.Length);
                }
                /// <summary>
                /// 发送文件给客户端进行下载
                /// </summary>
                /// <param name="client">与客户端建立的 TCP 连接</param>
                /// <param name="filePath">要发送的文件路径</param>
                public void SendFileToClient(string fileName, byte[] fileBytes)
                {
                    NetworkStream streamToClient = Client.GetStream();
                    StringBuilder response = new();
                    response.Append("HTTP/1.1 200 OK\r\n");
                    response.Append($"Content-Type: application/octet-stream\r\n");
                    response.Append("Access-Control-Allow-Origin: *\r\n");
                    response.Append($"Content-Disposition: attachment; filename=\"{fileName}\"\r\n");
                    response.Append($"Content-Length: {fileBytes.Length}\r\n");
                    response.Append("\r\n");
                    byte[] headerBytes = Encoding.UTF8.GetBytes(response.ToString());
                    streamToClient.Write(headerBytes, 0, headerBytes.Length);
                    streamToClient.Write(fileBytes, 0, fileBytes.Length);
                }
                /// <summary>
                /// 发送预检信息给客户端
                /// 可满足大部分api需求
                /// </summary>
                public void SendOptionsToClient()
                {
                    NetworkStream streamToServer = Client.GetStream();
                    StringBuilder response = new();
                    response.AppendLine("HTTP/1.1 200 OK");
                    response.AppendLine("Allow: GET, POST, PUT, DELETE");
                    response.AppendLine("Access-Control-Allow-Origin: *");
                    response.AppendLine("Access-Control-Allow-Methods: GET, POST, PUT, DELETE");
                    response.AppendLine("Access-Control-Allow-Headers: *");
                    response.AppendLine("Access-Control-Max-Age: 86400");
                    response.Append("\r\n");
                    byte[] headerBytes = Encoding.UTF8.GetBytes(response.ToString());
                    streamToServer.Write(headerBytes, 0, headerBytes.Length);
                }
            }
        }
    }
}
