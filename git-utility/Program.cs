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
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}