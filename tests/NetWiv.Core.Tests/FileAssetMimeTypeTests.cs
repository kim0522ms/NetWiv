using System.Buffers.Binary;
using System.Text;
using NetWiv;

internal static class FileAssetMimeTypeTests
{
    internal static Task Run()
    {
        FallbackMatchesTauriUtils();
        MatcherVectorsFollowInferOrder();
        CompoundOfficeClassIdsAreDistinguished();
        TruncatedAndMalformedInputsFallBack();
        return Task.CompletedTask;
    }

    private static void FallbackMatchesTauriUtils()
    {
        Check([], "asset.bin", "application/octet-stream");
        Check([], "asset.css", "text/css");
        Check([], "asset.less", "text/css");
        Check([], "asset.sass", "text/css");
        Check([], "asset.styl", "text/css");
        Check([], "asset.csv", "text/csv");
        Check([], "asset.ico", "image/vnd.microsoft.icon");
        Check([], "asset.js", "text/javascript");
        Check([], "asset.mjs", "text/javascript");
        Check([], "asset.json", "application/json");
        Check([], "asset.jsonld", "application/ld+json");
        Check([], "asset.mp4", "video/mp4");
        Check([], "asset.rtf", "application/rtf");
        Check([], "asset.svg", "image/svg+xml");
        Check([], "asset.txt", "text/plain");
        Check([], "asset.unknown", "text/html");
        Check([], "wry://tauri.app", "text/html");
        Check("<?xml version='1.0'?>"u8.ToArray(), "asset.svg", "image/svg+xml");
        Check("<?xml version='1.0'?>"u8.ToArray(), "asset.SVG", "text/xml");
        Check(Prefix(4, 0, 0x89, 0x50, 0x4e, 0x47), "asset.css", "image/png");
    }

    private static void MatcherVectorsFollowInferOrder()
    {
        var cases = new (byte[] Bytes, string Mime)[]
        {
            ("\0asm\x01\0\0\0"u8.ToArray(), "application/wasm"),
            (Prefix(53, 0, 0x7f, 0x45, 0x4c, 0x46), "application/x-executable"),
            ("MZ"u8.ToArray(), "application/vnd.microsoft.portable-executable"),
            (Prefix(8, 0, 0xca, 0xfe, 0xba, 0xbe, 0, 0, 0, 45), "application/java"),
            ("BC"u8.ToArray(), "application/x-llvm"),
            (Prefix(4, 0, 0xcf, 0xfa, 0xed, 0xfe), "application/x-mach-binary"),
            (Overlay(Prefix(37, 0, 0x64, 0x65, 0x78, 0x0a), 36, 0x70), "application/vnd.android.dex"),
            (Dey(), "application/vnd.android.dey"),
            (Prefix(3, 0, 0x30, 0x82), "application/x-x509-ca-cert"),
            (Prefix(3, 0, 0x64, 0x86), "application/x-executable"),
            (Prefix(12, 0, "-----BEGIN "u8.ToArray()), "application/x-x509-ca-cert"),
            (At(58, 0, new byte[] { 0x50, 0x4b, 3, 4 }, 30, "mimetypeapplication/epub+zip"u8.ToArray()), "application/epub+zip"),
            (At(68, 60, "BOOKMOBI"u8.ToArray()), "application/x-mobipocket-ebook"),
            (Prefix(3, 0, 0xff, 0xd8, 0xff), "image/jpeg"),
            (Prefix(13, 0, 0, 0, 0, 0x0c, 0x6a, 0x50, 0x20, 0x20, 0x0d, 0x0a, 0x87, 0x0a, 0), "image/jp2"),
            (Prefix(4, 0, 0x89, 0x50, 0x4e, 0x47), "image/png"),
            ("GIF"u8.ToArray(), "image/gif"),
            (At(12, 8, "WEBP"u8.ToArray()), "image/webp"),
            (At(11, 0, new byte[] { 0x49, 0x49, 0x2a, 0 }, 8, new byte[] { 0x43, 0x52, 2 }), "image/x-canon-cr2"),
            (At(10, 0, new byte[] { 0x49, 0x49, 0x2a, 0 }, 8, new byte[] { 1, 1 }), "image/tiff"),
            ("BM"u8.ToArray(), "image/bmp"),
            (Prefix(3, 0, 0x49, 0x49, 0xbc), "image/vnd.ms-photo"),
            ("8BPS"u8.ToArray(), "image/vnd.adobe.photoshop"),
            (Prefix(4, 0, 0, 0, 1, 0), "image/vnd.microsoft.icon"),
            (Ftyp("heic"), "image/heif"),
            (Ftyp("avif"), "image/avif"),
            (Prefix(3, 0, 0xff, 0x0a), "image/jxl"),
            (At(58, 0, new byte[] { 0x50, 0x4b, 3, 4 }, 30, "mimetypeimage/openraster"u8.ToArray()), "image/openraster"),
            (At(15, 0, "AT&TFORM"u8.ToArray(), 12, "DJV"u8.ToArray()), "image/vnd.djvu"),
            (Ftyp("mp42"), "video/mp4"),
            (At(11, 4, "ftypM4V"u8.ToArray()), "video/x-m4v"),
            (Prefix(16, 0, 0x1a, 0x45, 0xdf, 0xa3, 0x93, 0x42, 0x82, 0x88, 0x6d, 0x61, 0x74, 0x72, 0x6f, 0x73, 0x6b, 0x61), "video/x-matroska"),
            (Prefix(4, 0, 0x1a, 0x45, 0xdf, 0xa3), "video/webm"),
            (At(16, 4, "ftypqt  "u8.ToArray()), "video/quicktime"),
            (At(11, 0, "RIFF"u8.ToArray(), 8, "AVI"u8.ToArray()), "video/x-msvideo"),
            (Prefix(10, 0, 0x30, 0x26, 0xb2, 0x75, 0x8e, 0x66, 0xcf, 0x11, 0xa6, 0xd9), "video/x-ms-wmv"),
            (Prefix(4, 0, 0, 0, 1, 0xb0), "video/mpeg"),
            ("FLV\x01"u8.ToArray(), "video/x-flv"),
            ("MThd"u8.ToArray(), "audio/midi"),
            ("ID3"u8.ToArray(), "audio/mpeg"),
            (At(11, 4, "ftypM4A"u8.ToArray()), "audio/m4a"),
            (At(36, 0, "OggS"u8.ToArray(), 28, "OpusHead"u8.ToArray()), "audio/opus"),
            ("OggS"u8.ToArray(), "audio/ogg"),
            ("fLaC"u8.ToArray(), "audio/x-flac"),
            (At(12, 0, "RIFF"u8.ToArray(), 8, "WAVE"u8.ToArray()), "audio/x-wav"),
            (Prefix(12, 0, "#!AMR\n"u8.ToArray()), "audio/amr"),
            (Prefix(2, 0, 0xff, 0xf1), "audio/aac"),
            (At(12, 0, "FORM"u8.ToArray(), 8, "AIFF"u8.ToArray()), "audio/x-aiff"),
            (Prefix(5, 0, "DSD "u8.ToArray()), "audio/x-dsf"),
            (Prefix(5, 0, "MAC "u8.ToArray()), "audio/x-ape"),
            (Prefix(8, 0, 0x77, 0x4f, 0x46, 0x46, 0, 1, 0, 0), "application/font-woff"),
            (Prefix(8, 0, 0x77, 0x4f, 0x46, 0x32, 0, 1, 0, 0), "application/font-woff"),
            (Prefix(5, 0, 0, 1, 0, 0, 0), "application/font-sfnt"),
            (Prefix(5, 0, 0x4f, 0x54, 0x54, 0x4f, 0), "application/font-sfnt"),
            (At(35, 0, new byte[] { 0x50, 0x4b, 3, 4 }, 30, "word/"u8.ToArray()), "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            (At(33, 0, new byte[] { 0x50, 0x4b, 3, 4 }, 30, "xl/"u8.ToArray()), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            (At(34, 0, new byte[] { 0x50, 0x4b, 3, 4 }, 30, "ppt/"u8.ToArray()), "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
            (Odf("vnd.oasis.opendocument.text"), "application/vnd.oasis.opendocument.text"),
            (Odf("vnd.oasis.opendocument.spreadsheet"), "application/vnd.oasis.opendocument.spreadsheet"),
            (Odf("vnd.oasis.opendocument.presentation"), "application/vnd.oasis.opendocument.presentation"),
            (Prefix(4, 0, 0x50, 0x4b, 3, 4), "application/zip"),
            (At(262, 257, "ustar"u8.ToArray()), "application/x-tar"),
            (Prefix(9, 0, 0x50, 0x41, 0x52, 0x32, 0, 0x50, 0x4b, 0x54), "application/x-par2"),
            (Prefix(7, 0, 0x52, 0x61, 0x72, 0x21, 0x1a, 7, 1), "application/vnd.rar"),
            (Prefix(3, 0, 0x1f, 0x8b, 8), "application/gzip"),
            ("BZh"u8.ToArray(), "application/x-bzip2"),
            ("BZ3v1"u8.ToArray(), "application/vnd.bzip3"),
            (Prefix(6, 0, 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c), "application/x-7z-compressed"),
            (Prefix(6, 0, 0xfd, 0x37, 0x7a, 0x58, 0x5a, 0), "application/x-xz"),
            ("%PDF"u8.ToArray(), "application/pdf"),
            (Prefix(3, 0, 0x43, 0x57, 0x53), "application/x-shockwave-flash"),
            ("{\\rtf"u8.ToArray(), "application/rtf"),
            (At(36, 8, new byte[] { 2, 0, 1 }, 34, "LP"u8.ToArray()), "application/octet-stream"),
            ("%!"u8.ToArray(), "application/postscript"),
            ("SQLi"u8.ToArray(), "application/vnd.sqlite3"),
            (Prefix(4, 0, 0x4e, 0x45, 0x53, 0x1a), "application/x-nintendo-nes-rom"),
            ("Cr24"u8.ToArray(), "application/x-google-chrome-extension"),
            ("MSCF"u8.ToArray(), "application/vnd.ms-cab-compressed"),
            ("!<arch>\ndebian-binary"u8.ToArray(), "application/vnd.debian.binary-package"),
            ("!<arch>"u8.ToArray(), "application/x-unix-archive"),
            (Prefix(2, 0, 0x1f, 0x9d), "application/x-compress"),
            ("LZIP"u8.ToArray(), "application/x-lzip"),
            (Prefix(97, 0, 0xed, 0xab, 0xee, 0xdb), "application/x-rpm"),
            (At(132, 128, "DICM"u8.ToArray()), "application/dicom"),
            (Prefix(4, 0, 0x28, 0xb5, 0x2f, 0xfd), "application/zstd"),
            (Prefix(4, 0, 0x04, 0x22, 0x4d, 0x18), "application/x-lz4"),
            (Prefix(8, 0, 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1), "application/x-ole-storage"),
            (Prefix(2, 0, 0xc7, 0x71), "application/x-cpio"),
            (" \t<HTML >"u8.ToArray(), "text/html"),
            (Prefix(10, 0, 0xef, 0xbb, 0xbf, 0x3c, 0x3f, 0x58, 0x4d, 0x4c), "text/xml"),
            ("#!/bin/sh"u8.ToArray(), "text/x-shellscript")
        };

        foreach (var (bytes, mime) in cases) Check(bytes, "asset.unknown", mime);

        // Registration order, rather than the broad signature, determines these.
        Check(At(36, 0, "OggS"u8.ToArray(), 28, "OpusHead"u8.ToArray()), "x", "audio/opus");
        Check(Prefix(16, 0, 0x1a, 0x45, 0xdf, 0xa3, 0x93, 0x42, 0x82, 0x88, 0x6d, 0x61, 0x74, 0x72, 0x6f, 0x73, 0x6b, 0x61), "x", "video/x-matroska");
        Check(At(58, 0, new byte[] { 0x50, 0x4b, 3, 4 }, 30, "mimetypeapplication/epub+zip"u8.ToArray()), "x", "application/epub+zip");
    }

    private static void CompoundOfficeClassIdsAreDistinguished()
    {
        Check(CompoundFile("00020906-0000-0000-c000-000000000046"), "x", "application/msword");
        Check(CompoundFile("00020810-0000-0000-c000-000000000046"), "x", "application/vnd.ms-excel");
        Check(CompoundFile("64818d10-4f9b-11cf-86ea-00aa00b929e8"), "x", "application/vnd.ms-powerpoint");
    }

    private static void TruncatedAndMalformedInputsFallBack()
    {
        Check("<HTML"u8.ToArray(), "x", "text/html");
        Check("#!"u8.ToArray(), "x", "text/html");
        Check(Prefix(8, 0, 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1), "x", "application/x-ole-storage");
        Check(Prefix(8, 0, 0x50, 0x2a, 0x4d, 0x18, 1, 0, 0, 0), "x", "text/html");
        Check(Prefix(16, 0, 0, 0, 1, 0x66, 0x74, 0x79, 0x70, 0x68, 0x65, 0x69, 0x63), "x", "text/html");
    }

    internal static Task UpstreamCorpus()
    {
        var root = Environment.GetEnvironmentVariable("NETWIV_INFER_TESTDATA");
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("Set NETWIV_INFER_TESTDATA to the pinned upstream testdata directory.");
        }

        var cases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sample_elf"] = "application/x-executable",
            ["sample.exe"] = "application/vnd.microsoft.portable-executable",
            ["sample_mach_x86"] = "application/x-mach-binary",
            ["sample_mach_x64"] = "application/x-mach-binary",
            ["sample_mach_ppc"] = "application/x-mach-binary",
            ["sample_mach_fat"] = "application/x-mach-binary",
            ["sample.class"] = "application/java",
            ["sample.wasm"] = "application/wasm",
            ["sample.der"] = "application/x-x509-ca-cert",
            ["sample.pem"] = "application/x-x509-ca-cert",
            ["sample.tar.bz3"] = "application/vnd.bzip3",
            ["sample.db"] = "application/vnd.sqlite3",
            ["sample.tar.zst"] = "application/zstd",
            ["sample.tar.lz4"] = "application/x-lz4",
            ["sample.cpio"] = "application/x-cpio",
            ["sample.skippable.zst"] = "application/zstd",
            ["sample.par2"] = "application/x-par2",
            ["sample.mp3"] = "audio/mpeg",
            ["sample.dsf"] = "audio/x-dsf",
            ["sample.ape"] = "audio/x-ape",
            ["sample_48kbps.opus"] = "audio/opus",
            ["sample.epub"] = "application/epub+zip",
            ["sample.mobi"] = "application/x-mobipocket-ebook",
            ["sample.doc"] = "application/msword",
            ["sample.docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ["sample.xls"] = "application/vnd.ms-excel",
            ["sample.xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ["sample.ppt"] = "application/vnd.ms-powerpoint",
            ["sample.pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ["sample.ttf"] = "application/font-sfnt",
            ["sample.jpg"] = "image/jpeg",
            ["sample.png"] = "image/png",
            ["sample.gif"] = "image/gif",
            ["sample.tif"] = "image/tiff",
            ["sample2.tif"] = "image/tiff",
            ["sample3.tif"] = "image/tiff",
            ["sample4.tif"] = "image/tiff",
            ["sample5.tif"] = "image/tiff",
            ["sample.bmp"] = "image/bmp",
            ["sample.psd"] = "image/vnd.adobe.photoshop",
            ["sample.ico"] = "image/vnd.microsoft.icon",
            ["sample.heic"] = "image/heif",
            ["sample2.heic"] = "image/heif",
            ["sample.avif"] = "image/avif",
            ["spline_on_first_frame.jxl"] = "image/jxl",
            ["sample.ora"] = "image/openraster",
            ["sample_single.djvu"] = "image/vnd.djvu",
            ["sample_multi.djvu"] = "image/vnd.djvu",
            ["sample.odt"] = "application/vnd.oasis.opendocument.text",
            ["sample.ods"] = "application/vnd.oasis.opendocument.spreadsheet",
            ["sample.odp"] = "application/vnd.oasis.opendocument.presentation",
            ["sample.html"] = "text/html",
            ["sample.xml"] = "text/xml",
            ["sample2.xml"] = "text/xml",
            ["sample.sh"] = "text/x-shellscript",
            ["sample.mp4"] = "video/mp4",
            ["sample.mkv"] = "video/x-matroska",
            ["sample.webm"] = "video/webm",
            ["sample.mov"] = "video/quicktime",
            ["sample2.mov"] = "video/quicktime",
            ["sample.avi"] = "video/x-msvideo",
            ["sample.flv"] = "video/x-flv"
        };

        foreach (var (file, mime) in cases)
        {
            var path = Path.Combine(root, file);
            if (!File.Exists(path)) throw new Exception($"Pinned infer corpus file is missing: {path}");
            var bytes = File.ReadAllBytes(path);
            Check(bytes, path, mime);
            var prefixMime = file is "sample.doc" or "sample.xls" or "sample.ppt" ? "application/x-ole-storage" : mime;
            Check(bytes.AsSpan(0, Math.Min(8192, bytes.Length)).ToArray(), file, prefixMime);
        }

        var malformedFat = File.ReadAllBytes(Path.Combine(root, "sample.ppt"));
        malformedFat.AsSpan(512, 4).Clear();
        Check(malformedFat, "sample.ppt", "application/x-ole-storage");
        return Task.CompletedTask;
    }

    private static byte[] Dey()
    {
        var bytes = At(101, 0, "dey\n"u8.ToArray(), 40, "dex\n"u8.ToArray());
        bytes[76] = 0x70;
        return bytes;
    }

    private static byte[] Ftyp(string brand)
    {
        var bytes = At(16, 4, "ftyp"u8.ToArray(), 8, Encoding.ASCII.GetBytes(brand));
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 16);
        return bytes;
    }

    private static byte[] Odf(string kind) => At(96, 0, new byte[] { 0x50, 0x4b, 3, 4 }, 30, "mimetype"u8.ToArray(), 50, Encoding.ASCII.GetBytes(kind));

    // A minimal valid version-3 CFB with one root entry and one FAT sector.
    // The layout is the one written by cfb 0.7.3 for an empty compound file.
    private static byte[] CompoundFile(string classId)
    {
        var bytes = new byte[1536];
        byte[] headerMagic = [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1];
        headerMagic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(24), 0x003e);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 0xfffe);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(30), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(32), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 4096);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 0xfffffffe);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), 0xfffffffe);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(76), 0);
        bytes.AsSpan(80, 432).Fill(0xff);

        var root = bytes.AsSpan(1024, 128);
        Encoding.Unicode.GetBytes("Root Entry\0").CopyTo(root);
        BinaryPrimitives.WriteUInt16LittleEndian(root.Slice(64), 22);
        root[66] = 5;
        root[67] = 1;
        root.Slice(68, 12).Fill(0xff);
        WriteClassId(root.Slice(80), classId);
        BinaryPrimitives.WriteUInt32LittleEndian(root.Slice(116), 0xfffffffe);

        var fat = bytes.AsSpan(512, 512);
        BinaryPrimitives.WriteUInt32LittleEndian(fat, 0xfffffffd);
        BinaryPrimitives.WriteUInt32LittleEndian(fat.Slice(4), 0xfffffffe);
        fat.Slice(8).Fill(0xff);
        return bytes;
    }

    private static void WriteClassId(Span<byte> destination, string text)
    {
        var parts = text.Split('-');
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Convert.ToUInt32(parts[0], 16));
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4), Convert.ToUInt16(parts[1], 16));
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6), Convert.ToUInt16(parts[2], 16));
        Convert.FromHexString(parts[3] + parts[4]).CopyTo(destination.Slice(8));
    }

    private static byte[] Prefix(int length, int offset, params byte[] values) => At(length, offset, values);

    private static byte[] At(int length, params object[] writes)
    {
        var bytes = new byte[length];
        for (var index = 0; index < writes.Length; index += 2)
        {
            var offset = (int)writes[index];
            ((byte[])writes[index + 1]).CopyTo(bytes, offset);
        }
        return bytes;
    }

    private static byte[] Overlay(byte[] bytes, int offset, byte value)
    {
        bytes[offset] = value;
        return bytes;
    }

    private static void Check(byte[] bytes, string path, string expected)
    {
        var actual = FileAssetMimeType.Parse(bytes, path);
        if (actual != expected) throw new Exception($"MIME mismatch for {path}: expected {expected}, got {actual}.");
    }
}
