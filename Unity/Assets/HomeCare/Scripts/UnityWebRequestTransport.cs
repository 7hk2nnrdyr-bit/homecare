using System.Text;
using System.Threading.Tasks;
using HomeCare.Core.Firebase;
using UnityEngine.Networking;

namespace HomeCare.App
{
    /// <summary>コアの通信の依頼を、Unityの UnityWebRequest で送る。iPhone・Android・エディターで同じように動く。</summary>
    public class UnityWebRequestTransport : IHttpTransport
    {
        public Task<HttpResponseData> SendAsync(HttpRequestData request)
        {
            var completion = new TaskCompletionSource<HttpResponseData>();
            var web = new UnityWebRequest(request.Url, request.Method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 30,
            };
            if (request.Body != null)
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body)) { contentType = request.ContentType };
                web.SetRequestHeader("Content-Type", request.ContentType);
            }
            if (request.BearerToken != null)
            {
                web.SetRequestHeader("Authorization", "Bearer " + request.BearerToken);
            }

            web.SendWebRequest().completed += _ =>
            {
                // 通信そのものができなかったときは、結果番号を 0 にする
                var connected = web.result != UnityWebRequest.Result.ConnectionError;
                completion.SetResult(new HttpResponseData
                {
                    Status = connected ? (int)web.responseCode : 0,
                    Body = connected ? web.downloadHandler.text : web.error,
                });
                web.Dispose();
            };
            return completion.Task;
        }
    }
}
