using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

// Starts here. One run picks one game, then waits for Enter.
internal static class Program
{
    // Change this number if you want a shorter or longer wait.
    private const int DaysToWait = 20;

    public static int Main()
    {
        var code = 0;
        try
        {
            code = PickOne();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            code = 1;
        }

        // F5 closes the window the moment the program ends, so wait here.
        Console.WriteLine();
        Console.WriteLine("Press Enter to close.");
        Console.ReadLine();
        return code;
    }

    private static int PickOne()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var folder = FindListFolder();
        var games = ReadGameList(Path.Combine(folder, "gamelist.txt"));
        var memoryPath = Path.Combine(folder, "game-picker-state.json");
        var memory = LoadMemory(memoryPath);

        if (!AnyGameIsAllowed(games, memory, today))
        {
            Console.WriteLine(NoneAllowedMessage(games, memory));
            return 1;
        }

        // Keep drawing until the game is allowed.
        // A game is allowed if it has never been picked, or it was
        // picked at least DaysToWait days ago.
        string picked;
        var tries = 0;
        do
        {
            tries++;
            if (tries > 100_000)
            {
                Console.WriteLine("Gave up. Every draw was still inside the wait.");
                return 1;
            }

            picked = games[Random.Shared.Next(games.Count)];
        }
        while (!IsAllowed(memory, picked, today));

        var daysAgo = DaysSinceLastPick(memory, picked, today);
        memory.LastPickedOn[picked] = today;
        SaveMemory(memoryPath, memory);

        Console.WriteLine(picked);
        if (daysAgo == int.MaxValue)
            Console.WriteLine("Never picked before.");
        else if (daysAgo == 1)
            Console.WriteLine("Last picked 1 day ago.");
        else
            Console.WriteLine($"Last picked {daysAgo} days ago.");

        return 0;
    }

    // Visual Studio runs the program from bin\Debug\..., not from the project folder.
    // Walk up from there until we find the gamelist.txt that sits next to the .csproj,
    // so Rebuild does not throw the saved dates away.
    private static string FindListFolder()
    {
        string? fallback = null;
        var starts = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

        foreach (var start in starts)
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;

            for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir != null; dir = dir.Parent)
            {
                var listPath = Path.Combine(dir.FullName, "gamelist.txt");
                if (!File.Exists(listPath))
                    continue;

                fallback ??= dir.FullName;
                if (Directory.GetFiles(dir.FullName, "*.csproj").Length > 0)
                    return dir.FullName;
            }
        }

        if (fallback != null)
            return fallback;

        throw new FileNotFoundException(
            "Couldn't find gamelist.txt. In Solution Explorer, right-click the project (not the solution), choose Add, then Existing Item, and add gamelist.txt. It has to sit in the same folder as the .csproj file.");
    }

    private static bool IsAllowed(Memory memory, string title, DateOnly today)
    {
        return DaysSinceLastPick(memory, title, today) >= DaysToWait;
    }

    // int.MaxValue means it is not in the memory file, so it has never been picked.
    private static int DaysSinceLastPick(Memory memory, string title, DateOnly today)
    {
        if (!memory.LastPickedOn.TryGetValue(title, out var pickedOn))
            return int.MaxValue;

        var days = today.DayNumber - pickedOn.DayNumber;
        if (days < 0)
            return 0;

        return days;
    }

    private static bool AnyGameIsAllowed(List<string> games, Memory memory, DateOnly today)
    {
        foreach (var title in games)
        {
            if (IsAllowed(memory, title, today))
                return true;
        }

        return false;
    }

    // One title per line. Blank lines are skipped.
    // If the same title appears twice, only the first one is kept.
    private static List<string> ReadGameList(string path)
    {
        var games = new List<string>();
        var alreadyAdded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            var title = line.Trim();
            if (title.Length == 0)
                continue;
            if (!alreadyAdded.Add(title))
                continue;

            games.Add(title);
        }

        if (games.Count == 0)
            throw new InvalidOperationException("gamelist.txt has no game titles.");

        return games;
    }

    private static Memory LoadMemory(string path)
    {
        if (!File.Exists(path))
            return new Memory();

        Memory? memory;
        try
        {
            memory = JsonSerializer.Deserialize<Memory>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "Couldn't read game-picker-state.json. Delete that file to start over. " + ex.Message,
                ex);
        }

        if (memory == null)
            throw new InvalidOperationException("game-picker-state.json was empty. Delete it to start over.");

        memory.LastPickedOn ??= new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        return memory;
    }

    private static void SaveMemory(string path, Memory memory)
    {
        var text = JsonSerializer.Serialize(memory, JsonOptions);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, text);
        File.Move(tempPath, path, overwrite: true);
    }

    private static string NoneAllowedMessage(List<string> games, Memory memory)
    {
        DateOnly? nextOpenDate = null;

        foreach (var title in games)
        {
            if (!memory.LastPickedOn.TryGetValue(title, out var pickedOn))
                continue;

            var opensOn = pickedOn.AddDays(DaysToWait);
            if (nextOpenDate == null || opensOn < nextOpenDate)
                nextOpenDate = opensOn;
        }

        if (nextOpenDate == null)
            return "No game can be picked today.";

        return "Every game was picked within the last " + DaysToWait + " days. " +
               "The next one is allowed on " + nextOpenDate.Value.ToString("yyyy-MM-dd") + ".";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // This is the shape of game-picker-state.json.
    private sealed class Memory
    {
        public Dictionary<string, DateOnly> LastPickedOn { get; set; } = new(StringComparer.Ordinal);
    }
}