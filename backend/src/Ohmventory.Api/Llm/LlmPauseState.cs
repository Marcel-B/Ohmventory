using Microsoft.Extensions.Options;

namespace Ohmventory.Api.Llm;

/// <summary>
/// Pausenschalter für alle LLM-Funktionen. yue-guard setzt ihn über /api/llm/pause und /api/llm/resume,
/// wenn Tonwerk den Rechner braucht. Der Zustand liegt in einer Datei, damit er einen Neustart übersteht.
/// </summary>
public class LlmPauseState
{
    private readonly string _file;
    private readonly Lock _lock = new();
    private DateTimeOffset? _pausedSince;

    public LlmPauseState(IOptions<LlmOptions> options)
    {
        _file = Path.GetFullPath(options.Value.StateFile);
        if (File.Exists(_file))
            _pausedSince = DateTimeOffset.TryParse(File.ReadAllText(_file).Trim(), out var since) ? since : DateTimeOffset.UtcNow;
    }

    public bool IsPaused => _pausedSince is not null;
    public DateTimeOffset? PausedSince => _pausedSince;

    public void Pause()
    {
        lock (_lock)
        {
            if (_pausedSince is not null)
                return;
            _pausedSince = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, _pausedSince.Value.ToString("O"));
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _pausedSince = null;
            if (File.Exists(_file))
                File.Delete(_file);
        }
    }

    public void ThrowIfPaused()
    {
        if (IsPaused)
            throw new LlmPausedException();
    }
}
