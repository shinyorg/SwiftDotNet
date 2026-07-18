namespace SwiftDotNet;

/// <summary>
/// SwiftUI-mirrored state cell. Assigning <see cref="Value"/> (when it actually changes)
/// invalidates the running app and schedules a re-render — the same "mutate state → body
/// recomputes" loop as SwiftUI's <c>@State</c>.
///
/// Because it lives as a field on a composite <see cref="View"/> instance (which the runtime
/// keeps alive across renders), its value persists while <see cref="View.Body"/> is recomputed.
/// </summary>
public sealed class State<T>
{
    T _value;

    public State(T initialValue) => _value = initialValue;

    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
                return;
            _value = value;
            SwiftApp.RequestRender();
        }
    }

    public static implicit operator T(State<T> state) => state._value;

    public override string ToString() => _value?.ToString() ?? string.Empty;
}
