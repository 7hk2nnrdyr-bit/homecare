using System.Collections.Generic;

namespace HomeCare.App
{
    /// <summary>
    /// アプリに入っているマーカーの番号。Markers/HomeCareMarkers（マーカー画像の一覧）と同じにしておく。
    /// 印刷用のPDFは、リポジトリの markers/M01-print.pdf 〜 M10-print.pdf。
    /// 増やすときは markers/generate_markers.py で画像を作り、一覧にも足す。
    /// </summary>
    public static class MarkerCatalog
    {
        public static readonly IReadOnlyList<string> Ids = new[]
        {
            "M01", "M02", "M03", "M04", "M05", "M06", "M07", "M08", "M09", "M10",
        };
    }
}
