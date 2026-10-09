namespace MainGame.Scripts
{
    /// <summary>
    /// ActionScheduler tarafından yönetilen, iptal edilebilir bir unit aksiyonu (hareket, saldırı...).
    /// </summary>
    public interface IAction
    {
        void Cancel();
    }
}
