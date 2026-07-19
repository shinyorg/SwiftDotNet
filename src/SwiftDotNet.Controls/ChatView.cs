using System.Globalization;

namespace SwiftDotNet.Controls;

/// <summary>A chat message for <see cref="ChatView"/>: text, whether it's the local user's, an optional sender and time.</summary>
public sealed record ChatMessage(string Text, bool IsMine, string? Sender = null, DateTime? Time = null);

/// <summary>
/// A chat conversation UI — ported (without media attachments) from Shiny's <c>ChatView</c>. Pure
/// composite: outgoing/incoming bubbles, an optional typing indicator, a load-more affordance, and an
/// input bar with a bound draft + send button. Messages and the draft are supplied as state.
/// </summary>
public sealed class ChatView : View
{
    readonly IReadOnlyList<ChatMessage> _messages;
    readonly State<string> _draft;
    Action<string>? _onSend;
    Action? _onLoadMore;
    bool _typing;
    double _height = 420;

    public ChatView(IReadOnlyList<ChatMessage> messages, State<string> draft)
    {
        _messages = messages;
        _draft = draft;
    }

    /// <summary>Invoked with the draft text when the send button is tapped (clear the draft in your handler).</summary>
    public ChatView OnSend(Action<string> onSend) { _onSend = onSend; return this; }

    /// <summary>Shows a "Load earlier" affordance at the top that invokes this handler.</summary>
    public ChatView OnLoadMore(Action onLoadMore) { _onLoadMore = onLoadMore; return this; }

    /// <summary>Shows an animated typing indicator below the last message.</summary>
    public ChatView Typing(bool typing = true) { _typing = typing; return this; }

    public ChatView Height(double height) { _height = height; return this; }

    public override View Body
    {
        get
        {
            var thread = new List<View>();
            if (_onLoadMore is not null)
                thread.Add(new Text("Load earlier")
                    .Font(Font.Caption).ForegroundColor(ControlPalette.Accent(PillType.Info))
                    .Padding(8).OnTapGesture(_onLoadMore));

            foreach (var m in _messages) thread.Add(Bubble(m));
            if (_typing) thread.Add(TypingBubble());

            var messages = new ScrollView(new VStack(thread.ToArray()).Spacing(8).Alignment(HorizontalAlignment.Leading))
                .Frame(height: _height);

            var inputBar = new HStack(
                    new TextField("Message…", _draft),
                    new Button("Send", () => { if (_draft.Value.Length > 0) _onSend?.Invoke(_draft.Value); }))
                .Spacing(8)
                .Alignment(VerticalAlignment.Center);

            return new VStack(messages, inputBar).Spacing(10);
        }
    }

    View Bubble(ChatMessage m)
    {
        var (bg, fg) = m.IsMine
            ? (ControlPalette.Accent(PillType.Info), SwiftColor.Hex("#FFFFFF"))
            : (ControlPalette.SurfaceVariant, ControlPalette.OnSurface);

        var content = new List<View>();
        if (!m.IsMine && m.Sender is { } s)
            content.Add(new Text(s).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant));
        content.Add(new Text(m.Text).ForegroundColor(fg));
        if (m.Time is { } t)
            content.Add(new Text(t.ToString("HH:mm", CultureInfo.InvariantCulture))
                .Font(Font.Caption).ForegroundColor(m.IsMine ? SwiftColor.Hex("#E5E5EA") : ControlPalette.OnSurfaceVariant));

        var bubble = new VStack(content.ToArray())
            .Spacing(2)
            .Padding(horizontal: 12, vertical: 8)
            .Background(bg)
            .CornerRadius(16);

        // Right-align mine, left-align theirs, using a Spacer on the appropriate side.
        return m.IsMine
            ? new HStack(new Spacer(), bubble)
            : new HStack(bubble, new Spacer());
    }

    View TypingBubble() =>
        new HStack(
            new Text("• • •")
                .ForegroundColor(ControlPalette.OnSurfaceVariant)
                .Padding(horizontal: 12, vertical: 8)
                .Background(ControlPalette.SurfaceVariant)
                .CornerRadius(16)
                .Animation(Anim.EaseInOut(0.7).Repeating(autoreverse: true), on: true),
            new Spacer());
}
