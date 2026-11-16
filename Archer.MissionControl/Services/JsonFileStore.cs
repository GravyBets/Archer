using System.Text.Json;
using Archer.MissionControl.Domain;

namespace Archer.MissionControl.Services;

/// <summary>
/// Single-process, single-user local persistence. This is intentionally NOT a cloud database.
/// All access goes through a semaphore to prevent overwriting concurrent API operations.
/// </summary>
public sealed class JsonFileStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public JsonFileStore(IWebHostEnvironment env)
    {
        var directory = Environment.GetEnvironmentVariable("ARCHER_DATA_DIR");
        directory = string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(env.ContentRootPath, "Data")
            : Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "archer-state.json");
    }

    public async Task<T> ReadAsync<T>(Func<ArcherState, T> read, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return read(await LoadAsync(ct)); }
        finally { _gate.Release(); }
    }

    public async Task<T> UpdateAsync<T>(Func<ArcherState, T> update, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var state = await LoadAsync(ct);
            var result = update(state);
            await SaveAsync(state, ct);
            return result;
        }
        finally { _gate.Release(); }
    }

    public Task RecoverInterruptedTasksAsync() => UpdateAsync(state =>
    {
        foreach (var mission in state.Missions)
        {
            // Never resume paid API requests automatically after an app restart.
            mission.AutoRunEnabled = false;
            mission.AutoRunTasksRemaining = 0;
            var interrupted = mission.Status == "Running";
            foreach (var task in mission.Tasks.Where(t => t.Status == "Running"))
            {
                task.Status = "Pending";
                task.LastError = "Previous run was interrupted. You can run this task again.";
                interrupted = true;
            }
            if (interrupted) mission.Status = "Paused";
        }
        return true;
    });

    private async Task<ArcherState> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return new ArcherState();
        var json = await File.ReadAllTextAsync(_path, ct);
        return JsonSerializer.Deserialize<ArcherState>(json, JsonOptions) ?? new ArcherState();
    }

    private async Task SaveAsync(ArcherState state, CancellationToken ct)
    {
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state, JsonOptions), ct);
        File.Move(temporary, _path, overwrite: true);
    }
}
