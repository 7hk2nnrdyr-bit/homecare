namespace HomeCare.Core.Data
{
    /// <summary>
    /// 家のデータの保存場所の共通の形。
    /// 今は端末内のJSONファイル版とメモリ版。第3段階でクラウド版を足しても、他の部分は変えずに済む。
    /// </summary>
    public interface IHomeRepository
    {
        /// <summary>保存されたデータを読む。まだ何も保存されていなければ null を返す。</summary>
        HomeData Load();

        void Save(HomeData home);
    }

    /// <summary>メモリ上だけに保存する偶物。テストや動作確認に使う。</summary>
    public class InMemoryHomeRepository : IHomeRepository
    {
        private HomeData _saved;

        public HomeData Load() => _saved;

        public void Save(HomeData home) => _saved = home;
    }
}
