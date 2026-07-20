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
                thread.Add(new HStack(
                        new Text("Load earlier")
                            .Font(Font.Caption).ForegroundColor(ControlPalette.Accent(PillType.Info))
                            .Padding(Edge.Vertical, 6)
                            .OnTapGesture(_onLoadMore))
                    .Align(Alignment.Center));   // no spacers → the stack centres its content

            // Group consecutive messages from the same side: only the first of a run shows the sender.
            for (var i = 0; i < _messages.Count; i++)
            {
                var m = _messages[i];
                var startsRun = i == 0 || _messages[i - 1].IsMine != m.IsMine;
                thread.Add(Bubble(m, startsRun));
            }
            if (_typing) thread.Add(TypingBubble());

            // Both the scroll region AND the thread inside it must claim the full width, otherwise each
            // hugs its content and gets centred by its parent — leaving the row Spacers no slack to push
            // bubbles to the edges.
            var messages = new ScrollView(new VStack(thread.ToArray()).Spacing(6).Align(Alignment.Leading))
                .Frame(height: _height)
                .Align(Alignment.Leading);

            var canSend = _draft.Value.Trim().Length > 0;
            var inputBar = new HStack(
                    new TextField("Message…", _draft),
                    new Text("Send")
                        .Font(Font.Headline)
                        .ForegroundColor(canSend ? SwiftColor.Hex("#FFFFFF") : ControlPalette.OnSurfaceVariant)
                        .Padding(horizontal: 16, vertical: 10)
                        .Background(canSend ? ControlPalette.Accent(PillType.Info) : ControlPalette.SurfaceVariant)
                        .CornerRadius(18)
                        .OnTapGesture(() => { if (canSend) _onSend?.Invoke(_draft.Value); }))
                .Spacing(8)
                .Alignment(VerticalAlignment.Center);

            return new VStack(messages, inputBar).Spacing(10);
        }
    }

    View Bubble(ChatMessage m, bool startsRun)
    {
        var (bg, fg) = m.IsMine
            ? (ControlPalette.Accent(PillType.Info), SwiftColor.Hex("#FFFFFF"))
            : (ControlPalette.SurfaceVariant, ControlPalette.OnSurface);

        var content = new List<View>();
        if (!m.IsMine && startsRun && m.Sender is { } s)
            content.Add(new Text(s).Font(Font.Caption).ForegroundColor(ControlPalette.Accent(PillType.Info)));
        content.Add(new Text(m.Text).ForegroundColor(fg));
        if (m.Time is { } t)
            content.Add(new Text(t.ToString("HH:mm", CultureInfo.InvariantCulture))
                .Font(Font.Caption)
                .ForegroundColor(m.IsMine ? SwiftColor.Hex("#DCE9FF") : ControlPalette.OnSurfaceVariant));

        var bubble = new VStack(content.ToArray())
            .Spacing(3)
            .Alignment(m.IsMine ? HorizontalAlignment.Trailing : HorizontalAlignment.Leading)
            .Padding(horizontal: 14, vertical: 9)
            .Background(bg)
            .CornerRadius(18);

        // BOTH pieces are required: `.Align` makes the row claim the full width (a Spacer measures to zero,
        // so it can't widen the row by itself), and the Spacer then absorbs the slack to push the bubble to
        // its edge. `.Align` alone would centre the row's content, since a stack with no spacers starts its
        // cursor at free/2.
        return m.IsMine
            ? new HStack(new Spacer(), bubble).Align(Alignment.Trailing)
            : new HStack(bubble, new Spacer()).Align(Alignment.Leading);
    }

    // Three dots that breathe via the F4 repeating animation, staggered by per-dot delay.
    View TypingBubble()
    {
        var dots = new View[3];
        for (var i = 0; i < 3; i++)
            dots[i] = new Circle()
                .Frame(7, 7)
                .ForegroundColor(ControlPalette.OnSurfaceVariant)
                .Opacity(0.35)
                .Animation(new AnimationSpec(AnimationCurve.EaseInOut, 0.6, i * 0.18).Repeating(autoreverse: true), on: true);

        var bubble = new HStack(dots).Spacing(5)
            .Padding(horizontal: 14, vertical: 11)
            .Background(ControlPalette.SurfaceVariant)
            .CornerRadius(18);
        return new HStack(bubble, new Spacer()).Align(Alignment.Leading);
    }
}
