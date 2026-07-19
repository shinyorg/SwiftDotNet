using System;
using System.Collections.Generic;
using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>The ChatView — its own sample given how large the control is: bubbles, typing, load-more, input bar.</summary>
public sealed class ChatSample : View
{
    readonly State<string> _draft = State("");
    readonly State<List<ChatMessage>> _messages = State(new List<ChatMessage>
    {
        new("Hey! Are we still on for the launch?", IsMine: false, Sender: "Alex", Time: new DateTime(2026, 7, 15, 9, 3, 0)),
        new("Yep — 2pm works 👍", IsMine: true, Time: new DateTime(2026, 7, 15, 9, 4, 0)),
        new("Perfect. I'll bring the slides.", IsMine: false, Sender: "Alex", Time: new DateTime(2026, 7, 15, 9, 5, 0)),
    });

    public override View Body =>
        new VStack(
            new ChatView(_messages.Value, _draft)
                .Typing()
                .OnLoadMore(() => Toast.Show("Loading earlier…"))
                .Height(560)
                .OnSend(t =>
                {
                    _messages.Value = new List<ChatMessage>(_messages.Value) { new(t, IsMine: true) };
                    _draft.Value = "";
                })
        ).Padding(16).NavigationTitle("Chat");
}
