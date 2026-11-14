using RDCore.SDK.Workspace.Pictures;
using System.Buffers.Binary;

namespace RDCore.Runtime.Execution.External.Pictures;

/// <summary>
/// What <c>SavePicture</c> writes for a picture, as MS-VBA writes it.
/// </summary>
/// <remarks>
/// A metafile is written as it was read. A bitmap is written again from its pixels: a <c>BITMAPINFOHEADER</c> that states no resolution, its colour table, and
/// its rows, padded with zeros.
/// </remarks>
internal static class SavedPicture
{
    private const int FileHeader = 14;
    private const int InfoHeader = 40;

    /// <summary>
    /// The file <c>SavePicture</c> writes for <paramref name="picture"/>; <see langword="null"/> for a format it does not write.
    /// </summary>
    public static byte[]? BytesOf(StdPictureObject picture) => picture.Format switch
    {
        null => picture.IsNew ? [] : null,
        PictureFormat.Metafile or PictureFormat.EnhancedMetafile => picture.Bytes,
        PictureFormat.Bitmap => Bitmap(picture.Bytes),
        _ => null,
    };

    // a bitmap file whose DIB has a BITMAPINFOHEADER or a later one: the header's first 40 bytes, its colours, and its rows.
    private static byte[]? Bitmap(byte[] file)
    {
        var dib = file.AsSpan(FileHeader);
        var size = BinaryPrimitives.ReadInt32LittleEndian(dib);
        if (size < InfoHeader)
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(dib[8..]));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var used = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]);
        var colours = bits <= 8 ? (used is > 0 && used < 1 << bits ? used : 1 << bits) : 0;
        var stride = (int)(((long)width * bits + 31) / 32 * 4);
        var row = (int)(((long)width * bits + 7) / 8);
        var pixels = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(10));
        var table = FileHeader + size;
        if (table + colours * 4 > file.Length || pixels + (long)stride * height > file.Length)
        {
            return null;
        }

        var offset = FileHeader + InfoHeader + colours * 4;
        var saved = new byte[offset + stride * height];
        "BM"u8.CopyTo(saved);
        BinaryPrimitives.WriteInt32LittleEndian(saved.AsSpan(2), saved.Length);
        BinaryPrimitives.WriteInt32LittleEndian(saved.AsSpan(10), offset);

        var header = saved.AsSpan(FileHeader, InfoHeader);
        dib[..InfoHeader].CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header, InfoHeader);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], 0);

        file.AsSpan(table, colours * 4).CopyTo(saved.AsSpan(FileHeader + InfoHeader));
        for (var y = 0; y < height; y++)
        {
            file.AsSpan(pixels + y * stride, row).CopyTo(saved.AsSpan(offset + y * stride));
        }

        return saved;
    }
}
