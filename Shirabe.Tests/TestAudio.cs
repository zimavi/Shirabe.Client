namespace Shirabe.Tests;

internal static class TestAudio
{
    /// <summary>Writes a 440 Hz mono 16-bit 44.1 kHz sine wave.</summary>
    public static void WriteWav(string path, double seconds)
    {
        const int rate = 44100;
        var samples = (int)(rate * seconds);

        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        for (var i = 0; i < samples; i++)
            writer.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 8000));
    }
}
