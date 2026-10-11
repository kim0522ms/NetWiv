// Matcher order and predicates ported from infer 0.19.0, MIT.
// Compound-file validation ported from cfb 0.7.3, MIT.
// URI fallback ported from tauri-utils 2.9.3 mime_type.rs, MIT OR Apache-2.0.
using System.Buffers.Binary;

namespace NetWiv;

internal static class FileAssetMimeType
{
    internal static string Parse(ReadOnlySpan<byte> bytes, string path)
    {
        // Tauri deliberately bypasses inference for SVG because XML/HTML sniffing
        // can otherwise override the URI's web-compatible SVG type.
        if (!path.EndsWith(".svg", StringComparison.Ordinal) && TryInfer(bytes) is { } inferred)
            return inferred;

        return path.Split('.').Last() switch
        {
            "bin" => "application/octet-stream",
            "css" or "less" or "sass" or "styl" => "text/css",
            "csv" => "text/csv",
            "ico" => "image/vnd.microsoft.icon",
            "js" or "mjs" => "text/javascript",
            "json" => "application/json",
            "jsonld" => "application/ld+json",
            "mp4" => "video/mp4",
            "rtf" => "application/rtf",
            "svg" => "image/svg+xml",
            "txt" => "text/plain",
            _ => "text/html"
        };
    }

    private static string? TryInfer(ReadOnlySpan<byte> b)
    {
        // Keep this order identical to infer 0.19.0 src/map.rs. Several formats
        // intentionally overlap (EXE/DLL, EPUB/ZIP, MKV/WebM, Opus/OGG).
        if (Match(b, 0, "\0asm\x01\0\0\0"u8)) return "application/wasm";
        if (b.Length > 52 && Match(b, 0, new byte[] { 0x7f, 0x45, 0x4c, 0x46 })) return "application/x-executable";
        if (Match(b, 0, "MZ"u8)) return "application/vnd.microsoft.portable-executable";
        // is_dll is unreachable here because infer registers the identical EXE predicate first.
        if (IsJava(b)) return "application/java";
        if (Match(b, 0, "BC"u8)) return "application/x-llvm";
        if (IsMach(b)) return "application/x-mach-binary";
        if (IsDex(b)) return "application/vnd.android.dex";
        if (b.Length > 100 && Match(b, 0, "dey\n"u8) && IsDex(b.Slice(40, 60))) return "application/vnd.android.dey";
        if (b.Length > 2 && b[0] == 0x30 && b[1] == 0x82) return "application/x-x509-ca-cert";
        if (b.Length > 2 && ((b[0] == 0x64 && b[1] == 0x86) || (b[0] == 0x4c && b[1] == 0x01) || (b[0] == 0x00 && b[1] == 0x02))) return "application/x-executable";
        if (b.Length > 11 && Match(b, 0, "-----BEGIN "u8)) return "application/x-x509-ca-cert";

        if (IsEpub(b)) return "application/epub+zip";
        if (b.Length > 67 && Match(b, 60, "BOOKMOBI"u8)) return "application/x-mobipocket-ebook";

        if (b.Length > 2 && Match(b, 0, new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (b.Length > 12 && Match(b, 0, new byte[] { 0, 0, 0, 0x0c, 0x6a, 0x50, 0x20, 0x20, 0x0d, 0x0a, 0x87, 0x0a, 0 })) return "image/jp2";
        if (b.Length > 3 && Match(b, 0, new byte[] { 0x89, 0x50, 0x4e, 0x47 })) return "image/png";
        if (b.Length > 2 && Match(b, 0, "GIF"u8)) return "image/gif";
        if (b.Length > 11 && Match(b, 8, "WEBP"u8)) return "image/webp";
        if (IsCr2(b)) return "image/x-canon-cr2";
        if (IsTiff(b)) return "image/tiff";
        if (b.Length > 1 && Match(b, 0, "BM"u8)) return "image/bmp";
        if (b.Length > 2 && Match(b, 0, new byte[] { 0x49, 0x49, 0xbc })) return "image/vnd.ms-photo";
        if (b.Length > 3 && Match(b, 0, "8BPS"u8)) return "image/vnd.adobe.photoshop";
        if (b.Length > 3 && Match(b, 0, new byte[] { 0, 0, 1, 0 })) return "image/vnd.microsoft.icon";
        if (IsHeif(b)) return "image/heif";
        if (IsAvif(b)) return "image/avif";
        if (IsJxl(b)) return "image/jxl";
        if (IsOra(b)) return "image/openraster";
        if (b.Length > 14 && Match(b, 0, "AT&TFORM"u8) && Match(b, 12, "DJV"u8)) return "image/vnd.djvu";

        if (IsMp4(b)) return "video/mp4";
        if (b.Length > 10 && Match(b, 4, "ftypM4V"u8)) return "video/x-m4v";
        if (IsMkv(b)) return "video/x-matroska";
        if (b.Length > 3 && Match(b, 0, new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })) return "video/webm";
        if (IsMov(b)) return "video/quicktime";
        if (b.Length > 10 && Match(b, 0, "RIFF"u8) && Match(b, 8, "AVI"u8)) return "video/x-msvideo";
        if (b.Length > 9 && Match(b, 0, new byte[] { 0x30, 0x26, 0xb2, 0x75, 0x8e, 0x66, 0xcf, 0x11, 0xa6, 0xd9 })) return "video/x-ms-wmv";
        if (b.Length > 3 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] is >= 0xb0 and <= 0xbf) return "video/mpeg";
        if (b.Length > 3 && Match(b, 0, "FLV\x01"u8)) return "video/x-flv";

        if (b.Length > 3 && Match(b, 0, "MThd"u8)) return "audio/midi";
        if (b.Length > 2 && (Match(b, 0, "ID3"u8) || (b[0] == 0xff && b[1] == 0xfb))) return "audio/mpeg";
        if (b.Length > 10 && (Match(b, 4, "ftypM4A"u8) || Match(b, 0, "M4A "u8))) return "audio/m4a";
        if (IsOgg(b) && b.Length > 35 && Match(b, 28, "OpusHead"u8)) return "audio/opus";
        if (IsOgg(b)) return "audio/ogg";
        if (b.Length > 3 && Match(b, 0, "fLaC"u8)) return "audio/x-flac";
        if (b.Length > 11 && Match(b, 0, "RIFF"u8) && Match(b, 8, "WAVE"u8)) return "audio/x-wav";
        if (b.Length > 11 && Match(b, 0, "#!AMR\n"u8)) return "audio/amr";
        if (b.Length > 1 && b[0] == 0xff && b[1] is 0xf1 or 0xf9) return "audio/aac";
        if (b.Length > 11 && Match(b, 0, "FORM"u8) && Match(b, 8, "AIFF"u8)) return "audio/x-aiff";
        if (b.Length > 4 && Match(b, 0, "DSD "u8)) return "audio/x-dsf";
        if (b.Length > 4 && Match(b, 0, "MAC "u8)) return "audio/x-ape";

        if (b.Length > 7 && Match(b, 0, new byte[] { 0x77, 0x4f, 0x46, 0x46, 0, 1, 0, 0 })) return "application/font-woff";
        if (b.Length > 7 && Match(b, 0, new byte[] { 0x77, 0x4f, 0x46, 0x32, 0, 1, 0, 0 })) return "application/font-woff";
        if (b.Length > 4 && Match(b, 0, new byte[] { 0, 1, 0, 0, 0 })) return "application/font-sfnt";
        if (b.Length > 4 && Match(b, 0, new byte[] { 0x4f, 0x54, 0x54, 0x4f, 0 })) return "application/font-sfnt";

        var office = OfficeType(b);
        if (office == OfficeDocument.Doc) return "application/msword";
        if (office == OfficeDocument.Docx) return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        if (office == OfficeDocument.Xls) return "application/vnd.ms-excel";
        if (office == OfficeDocument.Xlsx) return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        if (office == OfficeDocument.Ppt) return "application/vnd.ms-powerpoint";
        if (office == OfficeDocument.Pptx) return "application/vnd.openxmlformats-officedocument.presentationml.presentation";

        var odf = OpenDocumentType(b);
        if (odf == OpenDocument.Text) return "application/vnd.oasis.opendocument.text";
        if (odf == OpenDocument.Spreadsheet) return "application/vnd.oasis.opendocument.spreadsheet";
        if (odf == OpenDocument.Presentation) return "application/vnd.oasis.opendocument.presentation";

        // EPUB is registered again in infer's archive section, but the book
        // registration above necessarily wins for the identical predicate.
        if (IsZip(b)) return "application/zip";
        if (b.Length > 261 && Match(b, 257, "ustar"u8)) return "application/x-tar";
        if (b.Length > 8 && Match(b, 0, new byte[] { 0x50, 0x41, 0x52, 0x32, 0, 0x50, 0x4b, 0x54 })) return "application/x-par2";
        if (b.Length > 6 && Match(b, 0, new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1a, 7 }) && b[6] is 0 or 1) return "application/vnd.rar";
        if (b.Length > 2 && Match(b, 0, new byte[] { 0x1f, 0x8b, 8 })) return "application/gzip";
        if (b.Length > 2 && Match(b, 0, "BZh"u8)) return "application/x-bzip2";
        if (b.Length > 4 && Match(b, 0, "BZ3v1"u8)) return "application/vnd.bzip3";
        if (b.Length > 5 && Match(b, 0, new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c })) return "application/x-7z-compressed";
        if (b.Length > 5 && Match(b, 0, new byte[] { 0xfd, 0x37, 0x7a, 0x58, 0x5a, 0 })) return "application/x-xz";
        if (b.Length > 3 && Match(b, 0, "%PDF"u8)) return "application/pdf";
        if (b.Length > 2 && b[0] is 0x43 or 0x46 && b[1] == 0x57 && b[2] == 0x53) return "application/x-shockwave-flash";
        if (b.Length > 4 && Match(b, 0, "{\\rtf"u8)) return "application/rtf";
        if (b.Length > 35 && Match(b, 34, "LP"u8) &&
            (Match(b, 8, new byte[] { 2, 0, 1 }) || Match(b, 8, new byte[] { 1, 0, 0 }) || Match(b, 8, new byte[] { 2, 0, 2 }))) return "application/octet-stream";
        if (b.Length > 1 && Match(b, 0, "%!"u8)) return "application/postscript";
        if (b.Length > 3 && Match(b, 0, "SQLi"u8)) return "application/vnd.sqlite3";
        if (b.Length > 3 && Match(b, 0, new byte[] { 0x4e, 0x45, 0x53, 0x1a })) return "application/x-nintendo-nes-rom";
        if (b.Length > 3 && Match(b, 0, "Cr24"u8)) return "application/x-google-chrome-extension";
        if (b.Length > 3 && (Match(b, 0, "MSCF"u8) || Match(b, 0, "ISc("u8))) return "application/vnd.ms-cab-compressed";
        if (b.Length > 20 && Match(b, 0, "!<arch>\ndebian-binary"u8)) return "application/vnd.debian.binary-package";
        if (b.Length > 6 && Match(b, 0, "!<arch>"u8)) return "application/x-unix-archive";
        if (b.Length > 1 && b[0] == 0x1f && b[1] is 0xa0 or 0x9d) return "application/x-compress";
        if (b.Length > 3 && Match(b, 0, "LZIP"u8)) return "application/x-lzip";
        if (b.Length > 96 && Match(b, 0, new byte[] { 0xed, 0xab, 0xee, 0xdb })) return "application/x-rpm";
        if (b.Length > 131 && Match(b, 128, "DICM"u8)) return "application/dicom";
        if (IsSkippableFrameFormat(b, new byte[] { 0x28, 0xb5, 0x2f, 0xfd })) return "application/zstd";
        if (IsSkippableFrameFormat(b, new byte[] { 0x04, 0x22, 0x4d, 0x18 })) return "application/x-lz4";
        if (b.Length > 7 && Match(b, 0, new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 })) return "application/x-ole-storage";
        if ((b.Length > 1 && ((b[0] == 0xc7 && b[1] == 0x71) || (b[0] == 0x71 && b[1] == 0xc7))) ||
            (b.Length > 6 && Match(b, 0, "070701"u8))) return "application/x-cpio";

        if (IsHtml(b)) return "text/html";
        if (IsXml(b)) return "text/xml";
        if (b.Length > 2 && Match(b, 0, "#!"u8)) return "text/x-shellscript";
        return null;
    }

    private static bool IsDex(ReadOnlySpan<byte> b) => b.Length > 36 && Match(b, 0, "dex\n"u8) && b[36] == 0x70;

    private static bool IsJava(ReadOnlySpan<byte> b) => b.Length >= 8 && Match(b, 0, new byte[] { 0xca, 0xfe, 0xba, 0xbe }) &&
        BinaryPrimitives.ReadUInt32BigEndian(b.Slice(4, 4)) >= 45 && BinaryPrimitives.ReadUInt16BigEndian(b.Slice(6, 2)) >= 45;

    private static bool IsMach(ReadOnlySpan<byte> b)
    {
        if (b.Length < 4) return false;
        if ((b[0] is 0xcf or 0xce && Match(b, 1, new byte[] { 0xfa, 0xed, 0xfe })) ||
            (Match(b, 0, new byte[] { 0xfe, 0xed, 0xfa }) && b[3] is 0xcf or 0xce)) return true;
        return b.Length >= 8 && Match(b, 0, new byte[] { 0xca, 0xfe, 0xba, 0xbe }) && BinaryPrimitives.ReadUInt32BigEndian(b.Slice(4, 4)) < 45;
    }

    private static bool IsEpub(ReadOnlySpan<byte> b) => b.Length > 57 && Match(b, 0, new byte[] { 0x50, 0x4b, 3, 4 }) && Match(b, 30, "mimetypeapplication/epub+zip"u8);
    private static bool IsOra(ReadOnlySpan<byte> b) => b.Length > 57 && Match(b, 0, new byte[] { 0x50, 0x4b, 3, 4 }) && Match(b, 30, "mimetypeimage/openraster"u8);

    private static bool IsCr2(ReadOnlySpan<byte> b) => b.Length > 10 && IsTiffHeader(b) && Match(b, 8, new byte[] { 0x43, 0x52, 2 });
    private static bool IsTiff(ReadOnlySpan<byte> b) => b.Length > 9 && IsTiffHeader(b) && b[8] != 0x43 && b[9] != 0x52 && !IsCr2(b);
    private static bool IsTiffHeader(ReadOnlySpan<byte> b) => Match(b, 0, new byte[] { 0x49, 0x49, 0x2a, 0 }) || Match(b, 0, new byte[] { 0x4d, 0x4d, 0, 0x2a });

    private static bool IsJxl(ReadOnlySpan<byte> b) =>
        (b.Length > 2 && Match(b, 0, new byte[] { 0xff, 0x0a })) ||
        (b.Length > 12 && Match(b, 0, new byte[] { 0, 0, 0, 0x0c, 0x4a, 0x58, 0x4c, 0x20, 0x0d, 0x0a, 0x87, 0x0a }));

    private static bool IsHeif(ReadOnlySpan<byte> b)
    {
        if (!TryGetFtyp(b, out var length, out var major)) return false;
        if (major is 0x68656963 or 0x68656978) return true;
        return major is 0x6d696631 or 0x6d736631 && CompatibleBrand(b, length, 0x68656963);
    }

    private static bool IsAvif(ReadOnlySpan<byte> b)
    {
        if (!TryGetFtyp(b, out var length, out var major)) return false;
        return major is 0x61766966 or 0x61766973 || CompatibleBrand(b, length, 0x61766966) || CompatibleBrand(b, length, 0x61766973);
    }

    private static bool TryGetFtyp(ReadOnlySpan<byte> b, out uint length, out uint major)
    {
        length = 0; major = 0;
        if (b.Length < 16 || !Match(b, 4, "ftyp"u8)) return false;
        length = BinaryPrimitives.ReadUInt32BigEndian(b);
        if ((ulong)b.Length < length) return false;
        major = BinaryPrimitives.ReadUInt32BigEndian(b.Slice(8, 4));
        return true;
    }

    private static bool CompatibleBrand(ReadOnlySpan<byte> b, uint length, uint wanted)
    {
        var count = Math.Max(0, (int)(length / 4) - 4);
        for (var i = 0; i < count && 16 + i * 4 + 4 <= b.Length; i++)
            if (BinaryPrimitives.ReadUInt32BigEndian(b.Slice(16 + i * 4, 4)) == wanted) return true;
        return false;
    }

    private static bool IsMp4(ReadOnlySpan<byte> b)
    {
        if (b.Length <= 11 || !Match(b, 4, "ftyp"u8)) return false;
        return BinaryPrimitives.ReadUInt32BigEndian(b.Slice(8, 4)) is
            0x61766331 or 0x64617368 or 0x69736f32 or 0x69736f33 or 0x69736f34 or 0x69736f35 or 0x69736f36 or 0x69736f6d or
            0x6d6d7034 or 0x6d703431 or 0x6d703432 or 0x6d703476 or 0x6d703731 or 0x4d534e56 or 0x4e444153 or 0x4e445343 or
            0x4e534443 or 0x4e445348 or 0x4e44534d or 0x4e445350 or 0x4e445353 or 0x4e445843 or 0x4e445848 or 0x4e44584d or
            0x4e445850 or 0x4e445853 or 0x46345620 or 0x46345020;
    }

    private static bool IsMkv(ReadOnlySpan<byte> b) =>
        (b.Length > 15 && Match(b, 0, new byte[] { 0x1a, 0x45, 0xdf, 0xa3, 0x93, 0x42, 0x82, 0x88 }) && Match(b, 8, "matroska"u8)) ||
        (b.Length > 38 && Match(b, 31, "matroska"u8));

    private static bool IsMov(ReadOnlySpan<byte> b) => b.Length > 15 &&
        (Match(b, 4, "ftypqt  "u8) || Match(b, 4, "moov"u8) || Match(b, 4, "mdat"u8) || Match(b, 12, "mdat"u8));

    private static bool IsOgg(ReadOnlySpan<byte> b) => b.Length > 3 && Match(b, 0, "OggS"u8);

    private enum OfficeDocument { None, Doc, Docx, Xls, Xlsx, Ppt, Pptx, OtherOpenXml }

    private static OfficeDocument OfficeType(ReadOnlySpan<byte> b)
    {
        if (TryReadOleRootClassId(b, out var classId))
        {
            if (classId is "00020810-0000-0000-c000-000000000046" or "00020820-0000-0000-c000-000000000046") return OfficeDocument.Xls;
            if (classId == "00020906-0000-0000-c000-000000000046") return OfficeDocument.Doc;
            if (classId == "64818d10-4f9b-11cf-86ea-00aa00b929e8") return OfficeDocument.Ppt;
        }
        return MsOfficeOpenXmlType(b);
    }

    private static OfficeDocument MsOfficeOpenXmlType(ReadOnlySpan<byte> b)
    {
        if (!Match(b, 0, new byte[] { 0x50, 0x4b, 3, 4 })) return OfficeDocument.None;
        var direct = OfficeAt(b, 30);
        if (direct != OfficeDocument.None) return direct;
        if (!Match(b, 30, "[Content_Types].xml"u8) && !Match(b, 30, "_rels/.rels"u8) && !Match(b, 30, "docProps"u8)) return OfficeDocument.None;
        var first = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(18, 4));
        if (first > uint.MaxValue - 49) return OfficeDocument.None;
        var start = (int)Math.Min((ulong)first + 49, int.MaxValue);
        var index = SearchLocalHeader(b, start, 6000);
        if (index < 0) return OfficeDocument.None;
        start += index + 30;
        index = SearchLocalHeader(b, start, 6000);
        if (index < 0) return OfficeDocument.None;
        start += index + 30;
        if (OfficeAt(b, start) == OfficeDocument.None) return OfficeDocument.None;
        start += 26;
        index = SearchLocalHeader(b, start, 6000);
        if (index < 0) return OfficeDocument.OtherOpenXml;
        start += index + 30;
        var fourth = OfficeAt(b, start);
        return fourth == OfficeDocument.None ? OfficeDocument.OtherOpenXml : fourth;
    }

    private static OfficeDocument OfficeAt(ReadOnlySpan<byte> b, int offset)
    {
        if (Match(b, offset, "word/"u8)) return OfficeDocument.Docx;
        if (Match(b, offset, "ppt/"u8)) return OfficeDocument.Pptx;
        if (Match(b, offset, "xl/"u8)) return OfficeDocument.Xlsx;
        return OfficeDocument.None;
    }

    private static int SearchLocalHeader(ReadOnlySpan<byte> b, int start, int range)
    {
        if (start < 0 || start >= b.Length) return -1;
        var end = Math.Min(b.Length, (long)start + range);
        return b.Slice(start, (int)end - start).IndexOf(new byte[] { 0x50, 0x4b, 3, 4 });
    }

    private enum OpenDocument { None, Text, Spreadsheet, Presentation }
    private static OpenDocument OpenDocumentType(ReadOnlySpan<byte> b)
    {
        if (!Match(b, 0, new byte[] { 0x50, 0x4b, 3, 4 }) || !Match(b, 30, "mimetype"u8)) return OpenDocument.None;
        if (Match(b, 50, "vnd.oasis.opendocument.text"u8)) return OpenDocument.Text;
        if (Match(b, 50, "vnd.oasis.opendocument.spreadsheet"u8)) return OpenDocument.Spreadsheet;
        if (Match(b, 50, "vnd.oasis.opendocument.presentation"u8)) return OpenDocument.Presentation;
        return OpenDocument.None;
    }

    private static bool IsZip(ReadOnlySpan<byte> b) => b.Length > 3 && Match(b, 0, "PK"u8) &&
        (((b[2] == 3 && b[3] == 4) || (b[2] == 5 && b[3] == 6) || (b[2] == 7 && b[3] == 8)) ||
         (b.Length > 7 && Match(b, 2, "00PK\x03\x04"u8)));

    private static bool IsSkippableFrameFormat(ReadOnlySpan<byte> b, ReadOnlySpan<byte> ordinaryMagic)
    {
        while (true)
        {
            if (b.Length > 3 && b.Slice(0, 4).SequenceEqual(ordinaryMagic)) return true;
            if (b.Length < 8) return false;
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(b);
            if ((magic & 0xfffffff0) != 0x184d2a50) return false;
            var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(4, 4));
            if ((ulong)b.Length < 8UL + dataLength) return false;
            b = b.Slice(checked(8 + (int)dataLength));
        }
    }

    private static bool IsHtml(ReadOnlySpan<byte> b)
    {
        b = TrimMimeWhitespace(b);
        ReadOnlySpan<string> tags = ["<!DOCTYPE HTML", "<HTML", "<HEAD", "<SCRIPT", "<IFRAME", "<H1", "<DIV", "<FONT", "<TABLE", "<A", "<STYLE", "<TITLE", "<B", "<BODY", "<BR", "<P", "<!--"];
        foreach (var tag in tags)
        {
            if (b.Length <= tag.Length || !StartsAsciiIgnoreCase(b, tag)) continue;
            if (b[tag.Length] is 0x20 or 0x3e) return true;
        }
        return false;
    }

    private static bool IsXml(ReadOnlySpan<byte> b)
    {
        b = TrimMimeWhitespace(b);
        while (b.Length >= 3)
        {
            if (Match(b, 0, new byte[] { 0xef, 0xbb, 0xbf })) b = b[3..];
            else if ((b[0] == 0xfe && b[1] == 0xff) || (b[0] == 0xff && b[1] == 0xfe)) b = b[2..];
            else break;
        }
        return StartsAsciiIgnoreCase(b, "<?xml");
    }

    private static ReadOnlySpan<byte> TrimMimeWhitespace(ReadOnlySpan<byte> b)
    {
        var index = 0;
        while (index < b.Length && b[index] is 0x09 or 0x0a or 0x0c or 0x0d or 0x20) index++;
        return b[index..];
    }

    private static bool StartsAsciiIgnoreCase(ReadOnlySpan<byte> b, string value)
    {
        if (b.Length < value.Length) return false;
        for (var i = 0; i < value.Length; i++)
        {
            var actual = b[i];
            var expected = (byte)value[i];
            if (actual >= (byte)'a' && actual <= (byte)'z') actual -= 32;
            if (expected >= (byte)'a' && expected <= (byte)'z') expected -= 32;
            if (actual != expected) return false;
        }
        return true;
    }

    // infer's std feature uses cfb 0.7.3 and compares the root storage CLSID.
    // This parser validates the CFB fields needed to safely locate that entry.
    private static bool TryReadOleRootClassId(ReadOnlySpan<byte> b, out string classId)
    {
        classId = string.Empty;
        if (b.Length < 512 || !Match(b, 0, new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 })) return false;
        var version = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(26, 2));
        if ((version != 3 && version != 4) || BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(28, 2)) != 0xfffe) return false;
        var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(30, 2));
        if (sectorShift != (version == 3 ? 9 : 12) || BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(32, 2)) != 6) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(56, 4)) != 4096) return false;
        var firstDirectorySector = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(48, 4));
        if (firstDirectorySector > 0xfffffffa || !ValidateCfbAllocation(b, 1 << sectorShift, firstDirectorySector)) return false;
        var directoryOffset = ((ulong)firstDirectorySector + 1) << sectorShift;
        if (directoryOffset + 128 > (ulong)b.Length || directoryOffset > int.MaxValue) return false;
        var root = b.Slice((int)directoryOffset, 128);
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(root.Slice(64, 2));
        if (nameLength > 64 || (nameLength & 1) != 0 || root[66] != 5 || root[67] > 1) return false;
        var d1 = BinaryPrimitives.ReadUInt32LittleEndian(root.Slice(80, 4));
        var d2 = BinaryPrimitives.ReadUInt16LittleEndian(root.Slice(84, 2));
        var d3 = BinaryPrimitives.ReadUInt16LittleEndian(root.Slice(86, 2));
        classId = $"{d1:x8}-{d2:x4}-{d3:x4}-{root[88]:x2}{root[89]:x2}-{root[90]:x2}{root[91]:x2}{root[92]:x2}{root[93]:x2}{root[94]:x2}{root[95]:x2}";
        return true;
    }

    private static bool ValidateCfbAllocation(ReadOnlySpan<byte> b, int sectorLength, uint firstDirectorySector)
    {
        const uint MaxRegularSector = 0xfffffffa;
        const uint DifatSector = 0xfffffffc;
        const uint FatSector = 0xfffffffd;
        const uint EndOfChain = 0xfffffffe;
        const uint FreeSector = 0xffffffff;

        var numSectors = checked((uint)(((ulong)b.Length + (uint)sectorLength - 1) / (uint)sectorLength - 1));
        var fatSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(44, 4));
        var difatSectorCount = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(72, 4));
        var fatSectors = new List<uint>();
        for (var index = 0; index < 109; index++)
        {
            var sector = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(76 + index * 4, 4));
            if (sector == FreeSector) break;
            if (sector > MaxRegularSector) return false;
            fatSectors.Add(sector);
        }

        var seenDifat = new HashSet<uint>();
        var currentDifat = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(68, 4));
        while (currentDifat != EndOfChain)
        {
            if (currentDifat > MaxRegularSector || currentDifat >= numSectors || !seenDifat.Add(currentDifat)) return false;
            if (!TrySector(b, sectorLength, currentDifat, out var sector)) return false;
            for (var index = 0; index < sectorLength / 4 - 1; index++)
            {
                var fatSector = BinaryPrimitives.ReadUInt32LittleEndian(sector.Slice(index * 4, 4));
                if (fatSector != FreeSector && fatSector > MaxRegularSector) return false;
                fatSectors.Add(fatSector);
            }
            currentDifat = BinaryPrimitives.ReadUInt32LittleEndian(sector.Slice(sectorLength - 4, 4));
        }
        if (seenDifat.Count != difatSectorCount) return false;
        while (fatSectors.Count != 0 && fatSectors[^1] == FreeSector) fatSectors.RemoveAt(fatSectors.Count - 1);
        if (fatSectors.Count != fatSectorCount) return false;

        var fat = new List<uint>(checked(fatSectors.Count * (sectorLength / 4)));
        foreach (var sectorId in fatSectors)
        {
            if (sectorId >= numSectors || !TrySector(b, sectorLength, sectorId, out var sector)) return false;
            for (var offset = 0; offset < sectorLength; offset += 4)
                fat.Add(BinaryPrimitives.ReadUInt32LittleEndian(sector.Slice(offset, 4)));
        }
        while (fat.Count > numSectors && fat.Count != 0 && fat[^1] == 0) fat.RemoveAt(fat.Count - 1);
        while (fat.Count != 0 && fat[^1] == FreeSector) fat.RemoveAt(fat.Count - 1);
        if (fat.Count > numSectors) return false;
        foreach (var sectorId in seenDifat)
            if (sectorId >= fat.Count || fat[(int)sectorId] != DifatSector) return false;
        foreach (var sectorId in fatSectors)
            if (sectorId >= fat.Count || fat[(int)sectorId] != FatSector) return false;

        var pointees = new HashSet<uint>();
        foreach (var next in fat)
        {
            if (next <= MaxRegularSector)
            {
                if (next >= fat.Count || !pointees.Add(next)) return false;
            }
            else if (next == 0xfffffffb) return false;
        }

        if (!ValidChain(fat, numSectors, firstDirectorySector, out _)) return false;
        var firstMiniFat = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(60, 4));
        var expectedMiniFatSectors = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(64, 4));
        if (!ValidChain(fat, numSectors, firstMiniFat, out var actualMiniFatSectors) || actualMiniFatSectors != expectedMiniFatSectors) return false;
        return true;
    }

    private static bool ValidChain(List<uint> fat, uint numSectors, uint first, out uint length)
    {
        const uint EndOfChain = 0xfffffffe;
        length = 0;
        var seen = new HashSet<uint>();
        for (var current = first; current != EndOfChain; current = fat[(int)current])
        {
            if (current > 0xfffffffa || current >= numSectors || current >= fat.Count || !seen.Add(current)) return false;
            length++;
        }
        return true;
    }

    private static bool TrySector(ReadOnlySpan<byte> b, int sectorLength, uint sectorId, out ReadOnlySpan<byte> sector)
    {
        var offset = ((ulong)sectorId + 1) * (uint)sectorLength;
        if (offset + (uint)sectorLength > (ulong)b.Length || offset > int.MaxValue)
        {
            sector = default;
            return false;
        }
        sector = b.Slice((int)offset, sectorLength);
        return true;
    }

    private static bool Match(ReadOnlySpan<byte> source, int offset, ReadOnlySpan<byte> expected) =>
        offset >= 0 && offset <= source.Length - expected.Length && source.Slice(offset, expected.Length).SequenceEqual(expected);
}
