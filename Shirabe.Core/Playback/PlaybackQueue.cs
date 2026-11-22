using System.ComponentModel;
using Shirabe.Core.Library;

namespace Shirabe.Core.Playback;

public enum RepeatMode
{
    Off,
    All,
    One,
}

/// <summary>One entry of the queue. A class, so the same song can be queued twice and still be told apart.</summary>
public sealed class QueueItem(TrackSummary track) : INotifyPropertyChanged
{
    private bool _isCurrent;

    public TrackSummary Track { get; } = track;

    /// <summary>True for the entry that is playing now. Maintained by <see cref="PlaybackQueue"/>.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        internal set
        {
            if (_isCurrent == value) return;
            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// What plays, in which order, and what comes after the current song. Pure logic: it never plays anything,
/// the caller asks "what next?" and plays the answer.
/// </summary>
/// <remarks>
/// Two lists: the natural order (as queued) and the play order (the natural order, or a shuffle of it).
/// Turning shuffle off restores the natural order around the current song.
/// </remarks>
public sealed class PlaybackQueue(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private List<QueueItem> _natural = [];
    private List<QueueItem> _playOrder = [];
    private QueueItem? _current;
    private int _position = -1; // index of _current in _playOrder; only meaningful while _current is not null
    private bool _shuffle;

    /// <summary>Raised when the list itself changed (queued, reordered, removed), not when only the position moved.</summary>
    public event EventHandler? Changed;

    /// <summary>The entries in play order, including the ones already played.</summary>
    public IReadOnlyList<QueueItem> Items => _playOrder;

    public QueueItem? Current => _current;

    /// <summary>Index of <see cref="Current"/> in <see cref="Items"/>, or -1.</summary>
    public int Position => _current is null ? -1 : _position;

    public RepeatMode Repeat { get; set; }

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            if (_shuffle == value) return;
            _shuffle = value;
            if (Current is { } current)
            {
                _playOrder = value ? ShuffledWithFirst(current) : [.. _natural];
                Move(_playOrder.IndexOf(current));
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Replaces the queue. With shuffle on, the chosen song still plays first.</summary>
    public void SetQueue(IReadOnlyList<TrackSummary> tracks, int startIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startIndex, tracks.Count);

        _natural = [.. tracks.Select(t => new QueueItem(t))];
        var start = _natural[startIndex];
        _playOrder = _shuffle ? ShuffledWithFirst(start) : [.. _natural];
        Move(_shuffle ? 0 : startIndex);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The song to play next, or null at the end of the queue (the position then stays on the last song).</summary>
    /// <param name="autoAdvance">The current song ended by itself. With <see cref="RepeatMode.One"/> that replays it;
    /// a manual "next" always moves on.</param>
    public QueueItem? MoveNext(bool autoAdvance)
    {
        if (Current is null) return null;
        if (autoAdvance && Repeat == RepeatMode.One) return Current;

        if (_position + 1 < _playOrder.Count)
        {
            Move(_position + 1);
            return Current;
        }

        if (Repeat != RepeatMode.All) return null;

        // A new round. With shuffle it is a new random order, and the song that just played does not open it again.
        if (_shuffle && _playOrder.Count > 1)
        {
            var last = _playOrder[^1];
            _playOrder = Shuffled(_natural);
            if (_playOrder[0] == last) (_playOrder[0], _playOrder[1]) = (_playOrder[1], _playOrder[0]);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        Move(0);
        return Current;
    }

    /// <summary>The song to play for "previous": the one before, wrapping with <see cref="RepeatMode.All"/>;
    /// at the very start the current song (to restart it).</summary>
    public QueueItem? MovePrevious()
    {
        if (Current is null) return null;

        if (_position > 0) Move(_position - 1);
        else if (Repeat == RepeatMode.All) Move(_playOrder.Count - 1);

        return Current;
    }

    public bool JumpTo(QueueItem item)
    {
        var index = _playOrder.IndexOf(item);
        if (index < 0) return false;

        Move(index);
        return true;
    }

    public void Enqueue(TrackSummary track)
    {
        var item = new QueueItem(track);
        _natural.Add(item);
        _playOrder.Add(item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Plays right after the current song (at the front of what is left, whatever the shuffle).</summary>
    public void PlayNext(TrackSummary track)
    {
        if (Current is not { } current)
        {
            Enqueue(track);
            return;
        }

        var item = new QueueItem(track);
        _natural.Insert(_natural.IndexOf(current) + 1, item);
        _playOrder.Insert(_position + 1, item);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes an entry. The song that is playing cannot be removed.</summary>
    public bool Remove(QueueItem item)
    {
        var index = _playOrder.IndexOf(item);
        if (index < 0 || item == Current) return false;

        _playOrder.RemoveAt(index);
        _natural.Remove(item);
        if (index < _position) _position--;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Empties the queue except for the song that is playing.</summary>
    public void ClearUpcoming()
    {
        if (_current is { } current)
        {
            _natural = [current];
            _playOrder = [current];
            _position = 0;
        }
        else
        {
            _natural = [];
            _playOrder = [];
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Always go through here: it keeps exactly one entry flagged as current, even when the play order was
    // replaced just before (shuffle, new round).
    private void Move(int position)
    {
        if (_current is not null) _current.IsCurrent = false;
        _position = position;
        _current = position >= 0 && position < _playOrder.Count ? _playOrder[position] : null;
        if (_current is not null) _current.IsCurrent = true;
    }

    private List<QueueItem> ShuffledWithFirst(QueueItem first) =>
        [first, .. Shuffled(_natural.Where(i => i != first))];

    private List<QueueItem> Shuffled(IEnumerable<QueueItem> items)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--) // Fisher-Yates
        {
            var j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
