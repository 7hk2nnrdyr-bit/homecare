using System.Threading.Tasks;

namespace HomeCare.Core.Firebase
{
    /// <summary>通信の依頼。Firebaseとのやり取りは、すべてこの形のHTTP通信で行う。</summary>
    public class HttpRequestData
    {
        public string Method = "GET";
        public string Url;

        /// <summary>送る中身。GETのときは null。</summary>
        public string Body;
        public string ContentType = "application/json";

        /// <summary>ログインで受け取った合い言葉（IDトークン）。不要なら null。</summary>
        public string BearerToken;
    }

    public class HttpResponseData
    {
        /// <summary>HTTPの結果番号（200など）。通信そのものができなかったときは 0。</summary>
        public int Status;
        public string Body;

        public bool IsSuccess => Status >= 200 && Status < 300;
    }

    /// <summary>
    /// 実際に通信する部分。Unityでは UnityWebRequest 版、テストでは .NET の HttpClient 版を使う。
    /// コアをUnityに依存させないため、通信の方法だけを外から渡す。
    /// </summary>
    public interface IHttpTransport
    {
        Task<HttpResponseData> SendAsync(HttpRequestData request);
    }
}
