using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace HomeCare.Core.Firebase
{
    /// <summary>Firestoreの1件（ドキュメント）。Path は "homes/{家ID}/rooms/{部屋ID}" の形。</summary>
    public class FirestoreDocument
    {
        public string Path;
        public Dictionary<string, object> Fields;

        public string Id => Path.Substring(Path.LastIndexOf('/') + 1);
    }

    /// <summary>まとめて保存するときの1件分。</summary>
    public class FirestoreWrite
    {
        public FirestoreDocument Document;

        /// <summary>この項目だけを書き換える（ほかの項目は残す）。null なら丸ごと置き換える。</summary>
        public List<string> OnlyFields;

        /// <summary>true なら、まだ無いときだけ作る（すでにあれば失敗させる）。</summary>
        public bool MustNotExist;

        /// <summary>true なら、すでにあるときだけ書き換える（無ければ失敗させる）。</summary>
        public bool MustExist;

        /// <summary>配列の項目に、まだ入っていない値を足す（例：memberUids に自分を足す）。</summary>
        public string AppendToArrayField;
        public List<object> AppendToArrayValues;

        /// <summary>配列の項目から、この値をすべて取り除く（例：memberUids から自分を消す）。</summary>
        public string RemoveFromArrayField;
        public List<object> RemoveFromArrayValues;

        /// <summary>true なら、書き込む代わりにドキュメントを消す（Document.Path だけを使う）。</summary>
        public bool Delete;
    }

    /// <summary>
    /// Firestore（クラウドのデータベース）の読み書き（REST API）。
    /// FirebaseのUnity用SDKを使わないので、Unity・テスト・どの端末でも同じコードで動く。
    /// </summary>
    public class FirestoreClient
    {
        /// <summary>1回のまとめ書きで送れる上限。</summary>
        public const int MaxWritesPerCommit = 500;

        private readonly FirebaseConfig _config;
        private readonly IHttpTransport _http;
        private readonly Func<Task<string>> _idToken;

        public FirestoreClient(FirebaseConfig config, IHttpTransport http, Func<Task<string>> idToken)
        {
            _config = config;
            _http = http;
            _idToken = idToken;
        }

        string DatabaseName => $"projects/{_config.ProjectId}/databases/(default)";
        string DocumentsUrl => $"{_config.FirestoreBaseUrl}/v1/{DatabaseName}/documents";

        /// <summary>1件を取得する。無ければ null。</summary>
        public async Task<FirestoreDocument> GetAsync(string path)
        {
            var response = await SendAsync("GET", $"{DocumentsUrl}/{path}", null);
            if (response.Status == 404)
            {
                return null;
            }
            EnsureSuccess(response);
            return ToDocument((Dictionary<string, object>)MiniJson.Parse(response.Body));
        }

        /// <summary>コレクション（例："homes/{家ID}/rooms"）の全件を取得する。</summary>
        public async Task<List<FirestoreDocument>> ListAsync(string collectionPath)
        {
            var documents = new List<FirestoreDocument>();
            string pageToken = null;
            do
            {
                var url = $"{DocumentsUrl}/{collectionPath}?pageSize=300";
                if (pageToken != null)
                {
                    url += "&pageToken=" + Uri.EscapeDataString(pageToken);
                }
                var response = await SendAsync("GET", url, null);
                EnsureSuccess(response);
                var body = (Dictionary<string, object>)MiniJson.Parse(response.Body);
                if (body.TryGetValue("documents", out var list))
                {
                    documents.AddRange(((List<object>)list).Select(d => ToDocument((Dictionary<string, object>)d)));
                }
                pageToken = body.TryGetValue("nextPageToken", out var next) ? (string)next : null;
            }
            while (!string.IsNullOrEmpty(pageToken));
            return documents;
        }

        /// <summary>一番上のコレクションから、配列の項目に値を含むものを探す（例：memberUids に自分のIDを含む家）。</summary>
        public async Task<List<FirestoreDocument>> FindArrayContainsAsync(string collectionId, string field, string value, int limit)
        {
            var query = new Dictionary<string, object>
            {
                ["structuredQuery"] = new Dictionary<string, object>
                {
                    ["from"] = new List<object> { new Dictionary<string, object> { ["collectionId"] = collectionId } },
                    ["where"] = new Dictionary<string, object>
                    {
                        ["fieldFilter"] = new Dictionary<string, object>
                        {
                            ["field"] = new Dictionary<string, object> { ["fieldPath"] = field },
                            ["op"] = "ARRAY_CONTAINS",
                            ["value"] = EncodeValue(value),
                        },
                    },
                    ["limit"] = (long)limit,
                },
            };
            var response = await SendAsync("POST", $"{DocumentsUrl}:runQuery", MiniJson.Write(query));
            EnsureSuccess(response);
            // 結果は [{ "document": {...} }, ...]。1件も無ければ [{ "readTime": ... }]
            return ((List<object>)MiniJson.Parse(response.Body))
                .Cast<Dictionary<string, object>>()
                .Where(row => row.ContainsKey("document"))
                .Select(row => ToDocument((Dictionary<string, object>)row["document"]))
                .ToList();
        }

        /// <summary>
        /// まとめて保存する。500件ずつ送り、その500件はすべて保存されるか、すべて失敗するかのどちらか。
        /// </summary>
        public async Task CommitAsync(IReadOnlyList<FirestoreWrite> writes)
        {
            for (var start = 0; start < writes.Count; start += MaxWritesPerCommit)
            {
                var chunk = writes.Skip(start).Take(MaxWritesPerCommit).Select(ToWriteJson).Cast<object>().ToList();
                var body = MiniJson.Write(new Dictionary<string, object> { ["writes"] = chunk });
                var response = await SendAsync("POST", $"{DocumentsUrl}:commit", body);
                EnsureSuccess(response);
            }
        }

        Dictionary<string, object> ToWriteJson(FirestoreWrite write)
        {
            if (write.Delete)
            {
                return new Dictionary<string, object> { ["delete"] = $"{DatabaseName}/documents/{write.Document.Path}" };
            }
            var json = new Dictionary<string, object>
            {
                ["update"] = new Dictionary<string, object>
                {
                    ["name"] = $"{DatabaseName}/documents/{write.Document.Path}",
                    ["fields"] = EncodeFields(write.Document.Fields),
                },
            };
            if (write.OnlyFields != null)
            {
                json["updateMask"] = new Dictionary<string, object> { ["fieldPaths"] = write.OnlyFields.Cast<object>().ToList() };
            }
            if (write.MustNotExist || write.MustExist)
            {
                json["currentDocument"] = new Dictionary<string, object> { ["exists"] = write.MustExist };
            }
            var transforms = new List<object>();
            if (write.AppendToArrayField != null)
            {
                transforms.Add(ArrayTransform(write.AppendToArrayField, "appendMissingElements", write.AppendToArrayValues));
            }
            if (write.RemoveFromArrayField != null)
            {
                transforms.Add(ArrayTransform(write.RemoveFromArrayField, "removeAllFromArray", write.RemoveFromArrayValues));
            }
            if (transforms.Count > 0)
            {
                json["updateTransforms"] = transforms;
            }
            return json;
        }

        static Dictionary<string, object> ArrayTransform(string field, string kind, List<object> values) =>
            new Dictionary<string, object>
            {
                ["fieldPath"] = field,
                [kind] = new Dictionary<string, object>
                {
                    ["values"] = values.Select(v => (object)EncodeValue(v)).ToList(),
                },
            };

        FirestoreDocument ToDocument(Dictionary<string, object> json)
        {
            var name = (string)json["name"];
            var prefix = $"{DatabaseName}/documents/";
            var fields = json.TryGetValue("fields", out var raw) ? DecodeFields((Dictionary<string, object>)raw) : new Dictionary<string, object>();
            return new FirestoreDocument
            {
                Path = name.StartsWith(prefix, StringComparison.Ordinal) ? name.Substring(prefix.Length) : name,
                Fields = fields,
            };
        }

        async Task<HttpResponseData> SendAsync(string method, string url, string body) =>
            await _http.SendAsync(new HttpRequestData
            {
                Method = method,
                Url = url,
                Body = body,
                BearerToken = await _idToken(),
            });

        static void EnsureSuccess(HttpResponseData response)
        {
            if (!response.IsSuccess)
            {
                throw FirebaseException.From(response);
            }
        }

        // ---- Firestoreの値の形（{"stringValue": "…"} など）との変換 ----

        public static Dictionary<string, object> EncodeFields(IDictionary<string, object> fields) =>
            fields.ToDictionary(pair => pair.Key, pair => (object)EncodeValue(pair.Value));

        public static Dictionary<string, object> DecodeFields(IDictionary<string, object> fields) =>
            fields.ToDictionary(pair => pair.Key, pair => DecodeValue((Dictionary<string, object>)pair.Value));

        public static Dictionary<string, object> EncodeValue(object value)
        {
            switch (value)
            {
                case null:
                    return new Dictionary<string, object> { ["nullValue"] = null };
                case string text:
                    return new Dictionary<string, object> { ["stringValue"] = text };
                case bool flag:
                    return new Dictionary<string, object> { ["booleanValue"] = flag };
                case int _:
                case long _:
                    // 整数は文字列で送る決まり
                    return new Dictionary<string, object> { ["integerValue"] = Convert.ToString(value, CultureInfo.InvariantCulture) };
                case float _:
                case double _:
                    return new Dictionary<string, object> { ["doubleValue"] = Convert.ToDouble(value, CultureInfo.InvariantCulture) };
                case IDictionary<string, object> map:
                    return new Dictionary<string, object>
                    {
                        ["mapValue"] = new Dictionary<string, object> { ["fields"] = EncodeFields(map) },
                    };
                case IList list:
                    return new Dictionary<string, object>
                    {
                        ["arrayValue"] = new Dictionary<string, object>
                        {
                            ["values"] = list.Cast<object>().Select(item => (object)EncodeValue(item)).ToList(),
                        },
                    };
                default:
                    throw new ArgumentException($"Firestoreに保存できない値です：{value.GetType().Name}");
            }
        }

        public static object DecodeValue(Dictionary<string, object> value)
        {
            if (value.TryGetValue("stringValue", out var text)) return text;
            if (value.TryGetValue("booleanValue", out var flag)) return flag;
            if (value.TryGetValue("integerValue", out var whole)) return Convert.ToInt64(whole, CultureInfo.InvariantCulture);
            if (value.TryGetValue("doubleValue", out var number)) return Convert.ToDouble(number, CultureInfo.InvariantCulture);
            if (value.TryGetValue("timestampValue", out var time)) return time;
            if (value.TryGetValue("mapValue", out var map))
            {
                var mapJson = (Dictionary<string, object>)map;
                return mapJson.TryGetValue("fields", out var fields)
                    ? DecodeFields((Dictionary<string, object>)fields)
                    : new Dictionary<string, object>();
            }
            if (value.TryGetValue("arrayValue", out var array))
            {
                var arrayJson = (Dictionary<string, object>)array;
                return arrayJson.TryGetValue("values", out var values)
                    ? ((List<object>)values).Select(item => DecodeValue((Dictionary<string, object>)item)).ToList()
                    : new List<object>();
            }
            return null;
        }
    }
}
