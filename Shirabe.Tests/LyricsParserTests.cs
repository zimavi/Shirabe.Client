using Shirabe.Core.Lyrics;

using Xunit;

namespace Shirabe.Tests;

public class LrcParserTests
{
    private static string[] Texts(IReadOnlyList<LyricLine> lines) => [.. lines.Select(l => l.Text)];

    private static double[] Seconds(IReadOnlyList<LyricLine> lines) =>
        [.. lines.Select(l => l.Start!.Value.TotalSeconds)];

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
                               <div><p begin="0:04.000" end="0:08.500">First line</p></div>
                               <div><p begin="00:10.2" end="00:12">Second line</p></div>
                             </body>
                           </tt>
                           """);

        Assert.Equal(["First line", "Second line"], lines.Select(l => l.Text));
        Assert.Equal([4, 10.2], lines.Select(l => l.Start!.Value.TotalSeconds));
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
                                            <p begin="2.100s" end="7.2s">
                                              <span begin="2.100s" end="5.800s">Hello</span> <span begin="5.900s" end="7.2s">wor</span><span begin="7.2s" end="7.3s">ld</span>
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

    // The structure of a real Better Lyrics export (composer namespace, head metadata, agents, a background vocal with timed
    // words, a word whose times overlap its neighbour's, lines that overlap a little), with invented words.
    [Fact]
    public void A_better_lyrics_export_reads_like_any_other_ttml_and_its_head_is_not_lyrics()
    {
        var lines = Parse($"""
                           {Header}
                           <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata" xmlns:ttp="http://www.w3.org/ns/ttml#parameter" xmlns:itunes="http://music.apple.com/lyric-ttml-internal" xmlns:composer="https://composer.betterlyrics.org/ttml" ttp:timeBase="media" xml:lang="en" itunes:timing="Word" composer:timing="Word">
                             <head>
                               <metadata>
                                 <ttm:title>A Song</ttm:title>
                                 <composer:meta key="artists" value="Someone"/>
                                 <ttm:agent xml:id="v1" type="person"><ttm:name>Someone</ttm:name></ttm:agent>
                                 <composer:groups><composer:group id="g1" label="Group 1" sharesTiming="true" ownTimingInstances="1,2"/></composer:groups>
                               </metadata>
                             </head>
                             <body>
                               <div>
                                 <p begin="0:31.557" end="0:34.180" itunes:key="L1" ttm:agent="v1"><span begin="0:31.557" end="0:32.177">Words</span> <span begin="0:32.177" end="0:32.678">come,</span> <span begin="0:33.077" end="0:34.180">slowly</span></p>
                                 <p begin="0:34.100" end="0:37.474" itunes:key="L2" ttm:agent="v1"><span begin="0:34.100" end="0:35.160">Second</span> <span begin="0:35.000" end="0:36.000">line</span> <span ttm:role="x-bg"><span begin="0:36.243" end="0:37.148">(softly)</span></span></p>
                               </div>
                             </body>
                           </tt>
                           """);

        // The 31 s before the first line is an interlude: a line of its own with no text.
        Assert.Equal(["", "Words come, slowly", "Second line (softly)"], lines.Select(l => l.Text));
        Assert.Equal([0, 31.557, 34.1], lines.Select(l => l.Start!.Value.TotalSeconds));
        Assert.Equal([3, 2], lines.Skip(1).Select(l => l.Words!.Count));
        Assert.Equal("(softly)", lines[2].Background);
        Assert.Equal(["(softly)"], lines[2].BackgroundWords!.Select(w => w.Text));
    }

    [Fact]
    public void A_background_vocal_keeps_its_own_word_times_with_parentheses_added_where_the_file_has_none()
    {
        var line = Assert.Single(Parse("""
                                       <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata"><body><div>
                                         <p begin="1s" end="6s"><span begin="1s" end="2s">Lead</span> <span ttm:role="x-bg"><span begin="3s" end="4s">ooh</span> <span begin="4s" end="5s">aah</span></span></p>
                                       </div></body></tt>
                                       """));

        Assert.Equal(["(ooh", "aah)"], line.BackgroundWords!.Select(w => w.Text));
        Assert.Equal([3, 4], line.BackgroundWords!.Select(w => w.Start.TotalSeconds));
        Assert.True(line.BackgroundWords![0].SpaceAfter);
    }

    [Fact]
    public void A_break_of_five_seconds_or_more_becomes_a_line_with_no_text_and_shorter_ones_do_not()
    {
        var lines = Parse("""
                          <tt xmlns="http://www.w3.org/ns/ttml"><body><div>
                            <p begin="1s" end="4s">one</p>
                            <p begin="7s" end="9s">two</p>
                            <p begin="14s" end="16s">three</p>
                            <p begin="15s" end="20s">overlaps three</p>
                            <p begin="21s" end="22s">four</p>
                          </div></body></tt>
                          """);

        // 4 s to 7 s is only 3 s; 9 s to 14 s is a pause of 5 s; the overlapping line keeps 20 s as the end of the sung part.
        Assert.Equal(["one", "two", "", "three", "overlaps three", "four"], lines.Select(l => l.Text));
        var pause = lines[2];
        Assert.Equal((9, 14), (pause.Start!.Value.TotalSeconds, pause.End!.Value.TotalSeconds));
    }

    [Fact]
    public void A_line_with_no_end_does_not_guess_a_pause_after_it()
    {
        var lines = Parse("""
                          <tt xmlns="http://www.w3.org/ns/ttml"><body><div>
                            <p begin="1s">no end given</p>
                            <p begin="30s" end="32s">far later</p>
                          </div></body></tt>
                          """);

        Assert.Equal(["no end given", "far later"], lines.Select(l => l.Text));
    }

    [Fact]
    public void In_a_duet_the_second_person_is_on_the_right_and_a_group_in_the_middle()
    {
        var lines = Parse("""
                          <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata"><head><metadata>
                            <ttm:agent xml:id="v1" type="person"><ttm:name>One</ttm:name></ttm:agent>
                            <ttm:agent xml:id="v2" type="person"><ttm:name>Two</ttm:name></ttm:agent>
                            <ttm:agent xml:id="v1000" type="group"><ttm:name>Harmony</ttm:name></ttm:agent>
                          </metadata></head><body><div>
                            <p begin="1s" end="2s" ttm:agent="v1">a</p><p begin="3s" end="4s" ttm:agent="v2">b</p>
                            <p begin="5s" end="6s" ttm:agent="v1000">c</p><p begin="7s" end="8s">d</p>
                          </div></body></tt>
                          """);

        Assert.Equal([LyricVoice.First, LyricVoice.Second, LyricVoice.Group, LyricVoice.First],
            lines.Select(l => l.Voice));
    }

    [Fact]
    public void Without_a_head_the_first_singer_met_is_the_first_voice()
    {
        var lines = Parse("""
                          <tt xmlns="http://www.w3.org/ns/ttml" xmlns:ttm="http://www.w3.org/ns/ttml#metadata"><body><div>
                            <p begin="1s" end="2s" ttm:agent="b">a</p><p begin="3s" end="4s" ttm:agent="a">b</p>
                          </div></body></tt>
                          """);

        Assert.Equal([LyricVoice.First, LyricVoice.Second], lines.Select(l => l.Voice));
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
    [InlineData(
        "<!DOCTYPE tt [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;\">]><tt xmlns=\"http://www.w3.org/ns/ttml\"><body><p begin=\"1s\">&b;</p></body></tt>")]
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

    private static IReadOnlyList<LyricLine> Overlapping() =>
    [
        new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), "lead"),
        new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(25), "echo"), // starts while "lead" is still going
        new(TimeSpan.FromSeconds(16), TimeSpan.FromSeconds(17), "short"), // and a third, inside both
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(35), "later"),
    ];

    private static int[] Active(IReadOnlyList<LyricLine> lines, double seconds)
    {
        var active = new List<int>();
        LyricsTimeline.Active(lines, TimeSpan.FromSeconds(seconds), active);
        return [.. active];
    }

    [Theory]
    [InlineData(5, new int[0])]
    [InlineData(12, new[] { 0 })]
    [InlineData(15, new[] { 1, 0 })] // the echo starts: the current line first, the lead still sung
    [InlineData(16.5, new[] { 2, 1, 0 })]
    [InlineData(18, new[] { 2, 1, 0 })] // "short" ended at 17 but is still the latest that started
    [InlineData(21, new[] { 2, 1 })] // the lead ended at 20; "short" stays current until the next line starts
    [InlineData(26, new[] { 2 })] // everything has ended: only the latest started stays
    [InlineData(31, new[] { 3 })]
    public void Every_line_that_is_still_being_sung_is_active_and_the_latest_started_stays_until_the_next(
        double seconds, int[] expected)
    {
        Assert.Equal(expected, Active(Overlapping(), seconds));
    }

    [Fact]
    public void Lines_without_an_end_stop_being_active_when_the_next_one_starts_as_before()
    {
        IReadOnlyList<LyricLine> lines =
            [new(TimeSpan.FromSeconds(1), null, "a"), new(TimeSpan.FromSeconds(5), null, "b")];

        Assert.Equal([1], Active(lines, 6));
        Assert.Equal([0], Active(lines, 3));
    }

    [Fact]
    public void Active_depends_on_the_position_alone_so_a_seek_back_lands_on_the_same_lines()
    {
        var lines = Overlapping();
        var first = Active(lines, 16.5);

        Active(lines, 31);
        Active(lines, 0);

        Assert.Equal(first, Active(lines, 16.5));
    }

    [Fact]
    public void No_lines_and_lines_without_times_have_no_current_line()
    {
        Assert.Equal(-1, LyricsTimeline.CurrentIndex([], TimeSpan.FromSeconds(5)));
        Assert.Equal(-1, LyricsTimeline.CurrentIndex([new LyricLine(null, null, "plain")], TimeSpan.FromSeconds(5)));
    }
}