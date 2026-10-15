using System.Runtime.InteropServices;

namespace CastleFight.Sim;

public enum CommandKind : byte
{
    PlaceBuilding = 1,

    /// <summary>Spawns <see cref="Command.Count"/> units in the player's staging area. For stress tests.</summary>
    DebugSpawn = 2,
}

/// <summary>
/// A player action. In lockstep multiplayer these are the only things sent over the network;
/// invalid commands are rejected identically by every client. Only integers, so the wire format is trivial.
/// </summary>
/// <param name="TileX">PlaceBuilding: the footprint's lowest tile (min corner).</param>
public readonly record struct Command(int PlayerId, CommandKind Kind, ushort TypeId, short TileX, short TileY, int Count)
{
    public static Command PlaceBuilding(int playerId, ushort typeId, int tileX, int tileY) =>
        new(playerId, CommandKind.PlaceBuilding, typeId, ClampToShort(tileX), ClampToShort(tileY), 0);

    public static Command DebugSpawn(int playerId, ushort typeId, int count) =>
        new(playerId, CommandKind.DebugSpawn, typeId, 0, 0, count);

    static short ClampToShort(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);
}

/// <summary>
/// Holds commands until the tick they are scheduled for. Commands are kept in canonical order
/// (by player, then in the order each player issued them) so every client applies them identically.
/// </summary>
public sealed class CommandSchedule
{
    readonly Dictionary<int, List<Command>> _byTick = new();
    readonly Stack<List<Command>> _pool = new();
    readonly List<Command> _current = new();

    public void Add(int tick, Command command)
    {
        if (!_byTick.TryGetValue(tick, out var list))
        {
            list = _pool.Count > 0 ? _pool.Pop() : new List<Command>();
            _byTick.Add(tick, list);
        }

        int index = list.Count;
        while (index > 0 && list[index - 1].PlayerId > command.PlayerId)
            index--;
        list.Insert(index, command);
    }

    /// <summary>Removes and returns the commands for <paramref name="tick"/>. The span is valid until the next call.</summary>
    public ReadOnlySpan<Command> TakeForTick(int tick)
    {
        _current.Clear();
        if (_byTick.Remove(tick, out var list))
        {
            _current.AddRange(list);
            list.Clear();
            _pool.Push(list);
        }
        return CollectionsMarshal.AsSpan(_current);
    }
}
