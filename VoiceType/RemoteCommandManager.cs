namespace VoiceType;

internal enum RemoteCommandKind
{
    Listen,
    Submit,
    Activate,
    Close
}

internal sealed class RemoteCommandInvocation
{
    public RemoteCommandInvocation(
        RemoteCommandKind command,
        string source,
        bool ignorePastedTextPrefix = false)
    {
        Command = command;
        Source = source;
        IgnorePastedTextPrefix = ignorePastedTextPrefix;
    }

    public RemoteCommandKind Command { get; }
    public string Source { get; }
    public bool IgnorePastedTextPrefix { get; }
}

internal sealed class RemoteCommandState
{
    public RemoteCommandState(
        bool isRecording,
        bool isTranscribing,
        bool isTranscribedPreviewActive,
        bool isShutdownRequested,
        bool isShuttingDown)
    {
        IsRecording = isRecording;
        IsTranscribing = isTranscribing;
        IsTranscribedPreviewActive = isTranscribedPreviewActive;
        IsShutdownRequested = isShutdownRequested;
        IsShuttingDown = isShuttingDown;
    }

    public bool IsRecording { get; }
    public bool IsTranscribing { get; }
    public bool IsTranscribedPreviewActive { get; }
    public bool IsShutdownRequested { get; }
    public bool IsShuttingDown { get; }
    public bool CanHandleRemoteCommands => !IsShuttingDown && !IsShutdownRequested;
}

internal delegate bool RemoteCommandExecutor(RemoteCommandInvocation invocation, RemoteCommandState state);

internal sealed class RemoteCommandBinding
{
    public RemoteCommandBinding(
        string id,
        RemoteCommandKind command,
        Func<RemoteCommandState, bool> canHandle,
        RemoteCommandExecutor execute,
        int priority = 0,
        bool enabled = true)
    {
        Id = id;
        Command = command;
        CanHandle = canHandle;
        Execute = execute;
        Priority = priority;
        Enabled = enabled;
    }

    public string Id { get; }
    public RemoteCommandKind Command { get; }
    public Func<RemoteCommandState, bool> CanHandle { get; }
    public RemoteCommandExecutor Execute { get; }
    public int Priority { get; }
    public bool Enabled { get; set; }
    internal int RegistrationOrder { get; set; } = -1;
}

internal sealed class RemoteCommandManager
{
    private readonly List<RemoteCommandBinding> _bindings = new();
    private int _nextBindingOrder;

    public void RegisterBinding(RemoteCommandBinding binding)
    {
        if (binding is null)
            throw new ArgumentNullException(nameof(binding));

        binding.RegistrationOrder = _nextBindingOrder++;
        _bindings.Add(binding);
    }

    public void ClearBindings()
    {
        _bindings.Clear();
        _nextBindingOrder = 0;
    }

    public bool TryHandleCommand(RemoteCommandInvocation invocation)
    {
        if (invocation is null)
            throw new ArgumentNullException(nameof(invocation));

        var state = new RemoteCommandState(
            isRecording: false,
            isTranscribing: false,
            isTranscribedPreviewActive: false,
            isShutdownRequested: false,
            isShuttingDown: false);
        return TryHandleCommand(invocation, state);
    }

    public bool TryHandleCommand(RemoteCommandInvocation invocation, RemoteCommandState state)
    {
        if (invocation is null)
            throw new ArgumentNullException(nameof(invocation));

        if (state is null)
            throw new ArgumentNullException(nameof(state));

        foreach (var binding in _bindings
                     .Where(
                         b => b.Enabled &&
                              b.Command == invocation.Command &&
                              b.CanHandle(state))
                     .OrderByDescending(b => b.Priority)
                     .ThenBy(b => b.RegistrationOrder))
        {
            if (binding.Execute(invocation, state))
                return true;
        }

        return false;
    }

    public IReadOnlyList<RemoteCommandBinding> GetBindings() => _bindings;
}

