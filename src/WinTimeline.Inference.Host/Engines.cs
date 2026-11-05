namespace WinTimeline.Inference.Host;

/// <summary>A native engine the host owns: it loads one model and runs prompts on it, one at a time.</summary>
internal interface IEngine : IDisposable
{
    /// <summary>Opens the model file.</summary>
    /// <param name="model">Its path.</param>
    void Load(string model);

    /// <summary>Runs a prompt, telling each piece of output as it comes.</summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="piece">Told each piece.</param>
    /// <returns>True when it finished, false when a cancel stopped it.</returns>
    bool Run(string prompt, Action<string> piece);

    /// <summary>Asks the run going on to stop; called from the reading thread.</summary>
    void Cancel();

    /// <summary>Clears a cancel before the next run.</summary>
    void Reset();
}

/// <summary>The engines the host can own, chosen by its arguments.</summary>
internal static class Engines
{
    /// <summary>The engine the arguments name.</summary>
    /// <param name="args">The host's arguments.</param>
    /// <returns>The engine.</returns>
    /// <exception cref="ArgumentException">The arguments name none this build has.</exception>
    public static IEngine Create(string[] args) => args switch
    {
        ["--fake"] => new FakeEngine(),

        // The bindings to llama.cpp's and whisper.cpp's C APIs are the rest of WT166, which waits
        // on the engines' download (WT101); each is pinned to the release it was written against,
        // as Revelei's host pins stable-diffusion.cpp's.
        ["--engine", string name, "--engines", _] => throw new ArgumentException($"This build has no binding to {name} yet."),
        _ => throw new ArgumentException("Usage: WinTimeline.Inference.Host --fake | --engine <llama.cpp|whisper.cpp> --engines <folder>"),
    };
}

/// <summary>
/// The engine the tests drive the protocol with: it answers a prompt with its words, one piece
/// each. Three prompts stand for what a native engine does wrong: "crash" ends the process the
/// way an access violation does, "hang" runs until it is cancelled, and "stuck" ignores the
/// cancel, so only ending the process stops it.
/// </summary>
internal sealed class FakeEngine : IEngine
{
    private volatile bool _cancelled;
    private string? _model;

    public void Load(string model)
    {
        if (!File.Exists(model))
        {
            throw new IOException($"There is no model at {model}.");
        }

        _model = model;
    }

    public bool Run(string prompt, Action<string> piece)
    {
        if (_model is null)
        {
            throw new InvalidOperationException("No model is loaded.");
        }

        switch (prompt)
        {
            case "crash":
                Environment.FailFast("The fake engine crashed, as a native one would.");
                return false;
            case "hang":
                while (!_cancelled)
                {
                    Thread.Sleep(20);
                }

                return false;
            case "stuck":
                Thread.Sleep(Timeout.Infinite);
                return false;
        }

        string[] words = prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
        {
            if (_cancelled)
            {
                return false;
            }

            piece(i == 0 ? words[i] : " " + words[i]);
        }

        return true;
    }

    public void Cancel() => _cancelled = true;

    public void Reset() => _cancelled = false;

    public void Dispose()
    {
        // Nothing native to free.
    }
}
