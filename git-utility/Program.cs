namespace GitUtility
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var gitManager = new GitManager();
                await gitManager.ExecuteCommand(args);
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Неожиданная ошибка: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}