using Shirabe.Core.Lyrics;
using Xunit;

namespace Shirabe.Tests;

public class LrcParserTests
{
    private static string[] Texts(IReadOnlyList<LyricLine> lines) => [.. lines.Select(l => l.Text)];

    private static double[] Seconds(IReadOnlyList<LyricLine> lines) => [.. lines.Select(l => l.Start!.Value.TotalSeconds)];

    [Fact]
    public void Stamps_with_minutes_seconds_and_a_fraction_of_one_two_or_three_digits_are_read()
    {
        var lines = LrcParser.Parse("[00:01]a\n[00:02.5]b\n[00:03.25]c\n[01:04.125]d");

        Assert.Equal([1, 2.5, 3.25, 64.125], Seconds(lines));
        Assert.Equal(["a", "b", "c", "d"], Texts(lines));
    }

    [Fact]
    public void Several_stamps_on_one_line_repeat_it_and_the_result_is_in_time_order()
    {
        var lines = LrcParser.Parse("[00:30.00][01:10.00]Chorus\n[00:10.00]Verse");

        Assert.Equal(["Verse", "Chorus", "Chorus"], Texts(lines));
        Assert.Equal([10, 30, 70], Seconds(lines));
    }

    [Fact]
    public void An_offset_moves_every_line_and_never_below_zero()
    {
        // The LRC convention: a positive offset shows the lyrics earlier.
        var lines = LrcParser.Parse("[offset:+500]\n[00:00.20]a\n[00:02.00]b");

        Assert.Equal([0, 1.5], Seconds(lines));
        Assert.Equal([2.5], Seconds(LrcParser.Parse("[offset:-500]\n[00:02.00]a")));
    }

    [Fact]
    public void Tags_that_describe_the_song_are_not_lyrics()
    {
        var lines = LrcParser.Parse("[ar:Someone]\n[ti:A Song]\n[length: 03:20]\n[00:01.00]words");

        Assert.Equal(["words"], Texts(lines));
    }

    [Fact]
    public void Word_stamps_are_dropped_from_the_text()
    {
        var lines = LrcParser.Parse("[00:01.00]<00:01.00>Hello <00:01.50>world");

        Assert.Equal(["Hello world"], Texts(lines));
    }

    [Fact]
    public void An_empty_line_with_a_stamp_is_kept_as_a_pause_and_rows_without_stamps_are_ignored_in_a_stamped_file()
    {
        var lines = LrcParser.Parse("intro text\n[00:01.00]a\n[00:05.00]\n[00:09.00]b");

        Assert.Equal(["a", "", "b"], Texts(lines));
    }

    [Fact]
    public void Two_lines_at_the_same_time_stay_in_the_order_they_were_written()
    {
        Assert.Equal(["first", "second"], Texts(LrcParser.Parse("[00:01.00]first\n[00:01.00]second")));
    }

    [Fact]
    public void Windows_line_endings_and_a_byte_order_mark_are_fine()
    {
        var lines = LrcParser.Parse("﻿[00:01.00]a\r\n[00:02.00]b\r\n");

        Assert.Equal(["a", "b"], Texts(lines));
    }

    [Fact]
    public void Text_without_stamps_is_plain_lyrics_with_stanza_breaks_kept_once()
    {
        var lines = LrcParser.Parse("\n\nline one\nline two\n\n\n\nline three\n\n");

        Assert.Equal(["line one", "line two", "", "line three"], Texts(lines));
        Assert.All(lines, l => Assert.Null(l.Start));
    }

    [Fact]
    public void A_section_label_in_square_brackets_is_a_line_of_plain_lyrics_not_a_tag()
    {
        Assert.Equal(["[Chorus]", "words"], Texts(LrcParser.Parse("[Chorus]\nwords")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n \r\n")]
    [InlineData("[ar:Someone]")]
    public void Nothing_to_show_gives_no_lines(string text)
    {
        Assert.Empty(LrcParser.Parse(text));
    }
}

public class TtmlParserTests
{
    private const string Header = """<?xml version="1.0" encoding="UTF-8"?>""";

    private static IReadOnlyList<LyricLine> Parse(string xml) => TtmlParser.Parse(xml)!;

    [Fact]
    public void Line_by_line_lyrics_are_read_in_document_order_across_divs()
    {
        var lines = Parse($"""
            {Header}
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" itunes:timing="Line">
              <body>
                <div><p begin="0:05.000" end="0:08.500">First line</p></div>
                <div><p begin="00:10.2" end="00:12">Second line</p></div>
              </body>
            </tt>
            """);

        Assert.Equal(["First line", "Second line"], lines.Select(l => l.Text));
        Assert.Equal([5, 10.2], lines.Select(l => l.Start!.Value.TotalSeconds));
        Assert.Equal([8.5, 12], lines.Select(l => l.End!.Value.TotalSeconds));
        Assert.All(lines, l => Assert.Null(l.Words));
    }

    [Fact]
    public void Word_by_word_lyrics_keep_each_words_time_and_the_spacing_the_file_has()
    {
        var line = Assert.Single(Parse($"""
            {Header}
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" itunes:timing="Word">
              <body><div>
                <p begin="5.100s" end="7.2s">
                  <span begin="5.100s" end="5.800s">Hello</span> <span begin="5.900s" end="7.2s">wor</span><span begin="7.2s" end="7.3s">ld</span>
                </p>
              </div></body>
            </tt>
            """));

        Assert.Equal("Hello world", line.Text);
        Assert.Equal(["Hello", "wor", "ld"], line.Words!.Select(w => w.Text));
        Assert.Equal([true, false], line.Words!.Take(2).Select(w => w.SpaceAfter)); // "wor" and "ld" are one word
        Assert.Equal(5.9, line.Words![1].Start.TotalSeconds, precision: 3);
        Assert.Equal(7.3, line.Words![2].End.TotalSeconds, precision: 3);
    }

    [Fact]
    public void A_line_without_its_own_times_takes_those_of_its_words()
    {
        var line = Assert.Single(Parse("""
            <tt xmlns="http://www.w3.org/ns/ttml"><body><div><p><span begin="2s" end="3s">a</span> <span begin="3s" end="4s">b</span></p></div></body></tt>
            """));

        Assert.Equal((2, 4), (line.Start!.Value.TotalSeconds, line.End!.Value.TotalSeconds));
    }

    [Fact]
    public void A_line_that_cannot_be_placed_in_time_is_skipped_and_lines_are_sorted()
    {
        var lines = Parse("""
            <tt xmlns="http://www.w3.org/ns/ttml"><body><div>
              <p begin="9s">late</p><p>no time at all</p><p begin="1s">early</p>
            </div></body></tt>
            """);

        Assert.Equal(["early", "late"], lines.Select(l => l.Text));
    }

    [Fact]
    public void Background_vocals_are_added_in_parentheses_and_translations_are_left_out()
    {
        var line = Assert.Single(Parse("""
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata"><body><div>
              <p begin="1s" end="5s">Lead words <span ttm:role="x-bg"><span begin="2s" end="3s">(ooh)</span></span><span ttm:role="x-translation" xml:lang="uk">Переклад</span></p>
            </div></body></tt>
            """));

        Assert.Equal("Lead words (ooh)", line.Text); // the file's own parentheses are not doubled
        Assert.Equal("(ooh)", line.Background);
        Assert.Null(line.Words); // the background vocal is not one of the line's words
    }

    [Fact]
    public void Background_vocals_without_parentheses_get_them()
    {
        var line = Assert.Single(Parse("""
            <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata"><body><div>
              <p begin="1s"><span begin="1s" end="2s">Lead</span> <span ttm:role="x-bg">ooh</span> <span ttm:role="x-bg">ah</span></p>
            </div></body></tt>
            """));

        Assert.Equal("Lead (ooh) (ah)", line.Text);
    }

    [Fact]
    public void Another_namespace_prefix_entities_cdata_and_a_line_break_are_handled()
    {
        var line = Assert.Single(Parse("""
            <ns:tt xmlns:ns="http://www.w3.org/ns/ttml"><ns:body><ns:div>
              <ns:p begin="1s">Tom &amp; <![CDATA[Jerry <3]]><ns:br/>again</ns:p>
            </ns:div></ns:body></ns:tt>
            """));

        Assert.Equal("Tom & Jerry <3 again", line.Text);
    }

    [Theory]
    [InlineData("12.5s", 12.5)]
    [InlineData("1500ms", 1.5)]
    [InlineData("2m", 120)]
    [InlineData("1h", 3600)]
    [InlineData("1:02:03.5", 3723.5)]
    [InlineData("02:03.5", 123.5)]
    [InlineData("00:07", 7)]
    [InlineData("7", 7)]
    public void Clock_and_offset_times_are_read(string text, double seconds)
    {
        Assert.True(TtmlParser.TryParseTime(text, out var time));
        Assert.Equal(seconds, time.TotalSeconds, precision: 3);
    }

    [Theory]
    [InlineData("10f")]
    [InlineData("500t")]
    [InlineData("00:00:07:12")]
    [InlineData("soon")]
    [InlineData("")]
    public void Frames_ticks_and_nonsense_are_refused_rather_than_guessed(string text)
    {
        Assert.False(TtmlParser.TryParseTime(text, out _));
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<tt><body><p begin=\"1s\">unclosed")]
    [InlineData("<html><body><p begin=\"1s\">x</p></body></html>")] // XML, but not TTML
    [InlineData("<!DOCTYPE tt [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;\">]><tt xmlns=\"http://www.w3.org/ns/ttml\"><body><p begin=\"1s\">&b;</p></body></tt>")]
    public void Anything_that_is_not_well_formed_ttml_or_has_a_doctype_gives_null(string text)
    {
        Assert.Null(TtmlParser.Parse(text));
    }
}

public class LyricsTimelineTests
{
    private static readonly IReadOnlyList<LyricLine> Lines =
    [
        new(TimeSpan.FromSeconds(5), null, "one"),
        new(TimeSpan.FromSeconds(10), null, "two"),
        new(TimeSpan.FromSeconds(10), null, "same time as two"),
        new(TimeSpan.FromSeconds(20), null, "three"),
    ];

    private static int At(double seconds) => LyricsTimeline.CurrentIndex(Lines, TimeSpan.FromSeconds(seconds));

    [Theory]
    [InlineData(0, -1)]
    [InlineData(4.999, -1)]
    [InlineData(5, 0)] // a line is current from its start
    [InlineData(9.9, 0)]
    [InlineData(10, 2)] // two lines at one time: the later one
    [InlineData(19.99, 2)]
    [InlineData(20, 3)]
    [InlineData(9999, 3)] // after the last line it stays current
    public void The_current_line_is_the_last_one_that_has_started(double position, int expected)
    {
        Assert.Equal(expected, At(position));
    }

    [Fact]
    public void Seeking_back_gives_the_earlier_line_and_the_same_position_always_gives_the_same_answer()
    {
        Assert.Equal(3, At(25));
        Assert.Equal(0, At(6));
        Assert.Equal(3, At(25));
    }

    [Fact]
    public void No_lines_and_lines_without_times_have_no_current_line()
    {
        Assert.Equal(-1, LyricsTimeline.CurrentIndex([], TimeSpan.FromSeconds(5)));
        Assert.Equal(-1, LyricsTimeline.CurrentIndex([new LyricLine(null, null, "plain")], TimeSpan.FromSeconds(5)));
    }
}
