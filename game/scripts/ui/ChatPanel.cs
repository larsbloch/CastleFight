using CastleFight.Net.Client;
using CastleFight.Net.Protocol;
using Godot;

namespace CastleFight.Game.UI;

/// <summary>Lobby chat: the log (names in team colours, relay notices in grey) and a line to type in.</summary>
public partial class ChatPanel : VBoxContainer
{
    LockstepClient _client = null!;
    RichTextLabel _log = null!;
    LineEdit _input = null!;

    public void Build(LockstepClient client)
    {
        _client = client;
        AddThemeConstantOverride("separation", 10);

        var well = new PanelContainer { ThemeTypeVariation = "InsetPanel", SizeFlagsVertical = SizeFlags.ExpandFill };
        _log = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SelectionEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        well.AddChild(_log);
        AddChild(well);

        _input = new LineEdit { PlaceholderText = "Say something…", MaxLength = ProtocolInfo.MaxChatLength, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _input.TextSubmitted += _ => Send();
        AddChild(UiBuilder.Row(_input, UiBuilder.Button("Send", Send, minWidth: 100)));

        foreach (var line in client.Chat)
            Append(line);
        client.ChatReceived += Append;
    }

    public override void _ExitTree() => _client.ChatReceived -= Append;

    void Send()
    {
        string text = _input.Text.Trim();
        if (text.Length > 0)
            _client.SendChat(text);
        _input.Clear();
        _input.GrabFocus();
    }

    void Append(ChatMessage line)
    {
        if (line.IsSystem)
        {
            _log.AppendText($"[i][color=#{UiTheme.Muted.ToHtml(false)}]{Escape(line.Text)}[/color][/i]\n");
            return;
        }
        var color = line.Team < UiTheme.TeamText.Length ? UiTheme.TeamText[line.Team] : UiTheme.Parchment;
        _log.AppendText($"[b][color=#{color.ToHtml(false)}]{Escape(line.Name)}:[/color][/b] {Escape(line.Text)}\n");
    }

    /// <summary>Players' text must not be read as BBCode.</summary>
    static string Escape(string text) => text.Replace("[", "[lb]");
}
