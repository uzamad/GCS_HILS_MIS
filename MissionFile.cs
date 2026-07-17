using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// ================================================================
//  MissionFile.cs  –  GCS_240626
//
//  Handles the .mfp (binary) and .mpr (legacy text) mission formats.
//
//  Binary .mfp layout (little-endian):
//
//  [HEADER — 128 bytes]
//    Offset   0  char[4]   Magic  "MSSN"
//    Offset   4  uint8     FormatVersion = 1
//    Offset   5  uint8     UAV_ID
//    Offset   6  uint8     WPCount  (total WP records in file)
//    Offset   7  uint8     Flags    (bit0=HasRunway, bit1=HasLanding)
//    Offset   8  char[64]  MissionName (null-padded)
//    Offset  72  char[16]  CreatedBy   (null-padded)
//    Offset  88  uint32    CreateTimestamp (Unix seconds, LE)
//    Offset  92  byte[36]  Reserved (zeros)
//
//  [GCS — 24 bytes]
//    Offset 128  double    GCS_Lat  (degrees)
//    Offset 136  double    GCS_Lon  (degrees)
//    Offset 144  double    GCS_Alt  (metres MSL)
//
//  [TAKEOFF — 112 bytes]
//    Offset 152  double×3  TOEnd1  (Lat, Lon, Alt)
//    Offset 176  double×3  TOEnd2
//    Offset 200  double×3  ClimbOutEnd1
//    Offset 224  double×3  ClimbOutEnd2
//    Offset 248  double    TORunwayWidth (m)
//    Offset 256  double    AbortTaxiDist (m)
//
//  [LANDING — 104 bytes]
//    Offset 264  double×3  LandEnd1
//    Offset 288  double×3  LandEnd2
//    Offset 312  double×3  LAPoint1  (approach point, end-1 direction)
//    Offset 336  double×3  LAPoint2  (approach point, end-2 direction)
//    Offset 360  double    LandRunwayWidth (m)
//
//  [WAYPOINTS — WPCount × 152 bytes]
//  Per record:
//    +0   uint8   DataValid
//    +1   uint8   Category  (0=WP, 1=BStation, 2=LandApproach, 3=TakeoffTaxi)
//    +2   uint16  WPNumber  (LE; LP use 6000+, TK use 7000+)
//    +4   uint8   LtrFlag
//    +5   uint8   SearchFlag
//    +6   uint8   POIFlag
//    +7   uint8   RadioSilenceFlag
//    +8   double  Lat  (degrees)
//    +16  double  Lon  (degrees)
//    +24  double  Alt  (metres MSL)
//    +32  double  CSpeed      (knots)
//    +40  double  LtrTime     (min)
//    +48  double  LtrRadius   (m)
//    +56  double  LtrSpeed    (knots)
//    +64  double  LtrAlt      (m)
//    +72  double  SearchBearing  (degrees)
//    +80  double  SearchWidth    (m)
//    +88  double  SearchLength   (m)   ← "SearchHeight" in .mpr
//    +96  double  SearchTime     (min)
//    +104 double  SearchSpeed    (knots)
//    +112 double  SearchAlt      (m)
//    +120 double  POILat  (degrees)
//    +128 double  POILon  (degrees)
//    +136 double  POIAlt  (m)
//    +144 byte[8] Reserved
//    = 152 bytes
//
//  [CRC-32 — 4 bytes]
//    Standard CRC-32 (poly 0xEDB88320) of all preceding bytes.
// ================================================================

namespace GCS_240626
{
    // ── WP category ───────────────────────────────────────────────────────
    public enum WPCategory : byte
    {
        Waypoint     = 0,   // normal mission WP
        BStation     = 1,   // GCS / Home (WP0)
        LandApproach = 2,   // LP6000+ in legacy .mpr
        TakeoffTaxi  = 3,   // TP7000+ in legacy .mpr
    }

    // ── LLA helper ────────────────────────────────────────────────────────
    public struct LLAPoint
    {
        public double Lat, Lon, Alt;   // degrees, degrees, metres
    }

    // ── Per-waypoint record ───────────────────────────────────────────────
    public class MissionWP
    {
        public bool        DataValid;
        public WPCategory  Category;
        public ushort      Number;

        public bool        LtrFlag;
        public bool        SearchFlag;
        public bool        POIFlag;
        public bool        RadioSilenceFlag;

        public double Lat, Lon, Alt;   // degrees, metres
        public double CSpeed;          // knots

        // Loiter
        public double LtrTime, LtrRadius, LtrSpeed, LtrAlt;

        // Search pattern
        public double SearchBearing, SearchWidth, SearchLength;
        public double SearchTime, SearchSpeed, SearchAlt;

        // Point-of-interest
        public double POILat, POILon, POIAlt;
    }

    // ── Complete mission ──────────────────────────────────────────────────
    public class MissionData
    {
        public string   Name        = "";
        public string   CreatedBy   = "";
        public DateTime CreateDate  = DateTime.Now;
        public int      UAV_ID;

        // GCS / home position
        public LLAPoint GCS;

        // Takeoff
        public LLAPoint TORunwayEnd1, TORunwayEnd2;
        public LLAPoint ClimbOutEnd1, ClimbOutEnd2;
        public double   TORunwayWidth;
        public double   AbortTaxiDist;

        // Landing
        public LLAPoint LandRunwayEnd1, LandRunwayEnd2;
        public LLAPoint LAPoint1, LAPoint2;
        public double   LandRunwayWidth;

        // Waypoints (all categories)
        public readonly List<MissionWP> WayPoints = new List<MissionWP>();

        /// <summary>Mission waypoints only (WP0 = BStation, WP1+ = Waypoint).</summary>
        public IEnumerable<MissionWP> MissionWPs()
        {
            foreach (var wp in WayPoints)
                if (wp.Category == WPCategory.Waypoint || wp.Category == WPCategory.BStation)
                    yield return wp;
        }

        public bool HasRunwayData   => TORunwayEnd1.Lat != 0 || TORunwayEnd1.Lon != 0;
        public bool HasLandingData  => LandRunwayEnd1.Lat != 0 || LandRunwayEnd1.Lon != 0;
        public bool HasApproachData => LAPoint1.Lat != 0 || LAPoint1.Lon != 0;
    }

    // ── I/O class ─────────────────────────────────────────────────────────
    public static class MissionFile
    {
        private static readonly byte[] MAGIC   = { (byte)'M', (byte)'S', (byte)'S', (byte)'N' };
        private const byte FORMAT_VERSION = 1;

        // ── Auto-detect and read ─────────────────────────────────────────
        /// <summary>Load mission file — detects binary vs text by magic bytes, not by extension.</summary>
        public static MissionData Read(string path)
        {
            // Always detect by content: binary .mfp starts with "MSSN"; everything else is text .mpr
            byte[] head = new byte[4];
            using (var fs = File.OpenRead(path)) fs.Read(head, 0, 4);
            if (head[0] == 'M' && head[1] == 'S' && head[2] == 'S' && head[3] == 'N')
                return ReadMfp(path);
            return ReadMpr(path);
        }

        // ════════════════════════════════════════════════════════════════
        //  Binary .mfp reader/writer
        // ════════════════════════════════════════════════════════════════
        public static MissionData ReadMfp(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            if (raw.Length < 132)
                throw new InvalidDataException("File too short to be a valid .mfp.");

            // Verify magic
            if (raw[0] != 'M' || raw[1] != 'S' || raw[2] != 'S' || raw[3] != 'N')
                throw new InvalidDataException("Bad magic — not an .mfp mission file.");

            // Verify CRC-32 (last 4 bytes)
            uint stored = BitConverter.ToUInt32(raw, raw.Length - 4);
            uint calc   = Crc32(raw, 0, raw.Length - 4);
            if (stored != calc)
                throw new InvalidDataException(
                    $"CRC-32 mismatch: stored 0x{stored:X8}, computed 0x{calc:X8}.");

            using (var ms = new MemoryStream(raw))
            using (var r  = new BinaryReader(ms, Encoding.ASCII))
            {
                r.BaseStream.Seek(4, SeekOrigin.Begin); // skip magic
                byte version = r.ReadByte();
                var  md      = new MissionData();
                md.UAV_ID    = r.ReadByte();
                int wpCount  = r.ReadByte();
                /*flags*/     r.ReadByte();

                md.Name      = ReadFixedString(r, 64);
                md.CreatedBy = ReadFixedString(r, 16);
                uint ts      = r.ReadUInt32();
                md.CreateDate = DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime;
                r.BaseStream.Seek(92 + 36, SeekOrigin.Begin); // skip to GCS @ 128

                // GCS
                md.GCS = ReadLLA(r);

                // Takeoff @ 152
                r.BaseStream.Seek(152, SeekOrigin.Begin);
                md.TORunwayEnd1  = ReadLLA(r);
                md.TORunwayEnd2  = ReadLLA(r);
                md.ClimbOutEnd1  = ReadLLA(r);
                md.ClimbOutEnd2  = ReadLLA(r);
                md.TORunwayWidth = r.ReadDouble();
                md.AbortTaxiDist = r.ReadDouble();

                // Landing @ 264
                r.BaseStream.Seek(264, SeekOrigin.Begin);
                md.LandRunwayEnd1 = ReadLLA(r);
                md.LandRunwayEnd2 = ReadLLA(r);
                md.LAPoint1       = ReadLLA(r);
                md.LAPoint2       = ReadLLA(r);
                md.LandRunwayWidth = r.ReadDouble();

                // Waypoints @ 368
                r.BaseStream.Seek(368, SeekOrigin.Begin);
                for (int i = 0; i < wpCount; i++)
                {
                    long wp_start = r.BaseStream.Position;
                    var wp         = new MissionWP();
                    wp.DataValid          = r.ReadByte() != 0;
                    wp.Category           = (WPCategory)r.ReadByte();
                    wp.Number             = r.ReadUInt16();
                    wp.LtrFlag            = r.ReadByte() != 0;
                    wp.SearchFlag         = r.ReadByte() != 0;
                    wp.POIFlag            = r.ReadByte() != 0;
                    wp.RadioSilenceFlag   = r.ReadByte() != 0;
                    wp.Lat            = r.ReadDouble();
                    wp.Lon            = r.ReadDouble();
                    wp.Alt            = r.ReadDouble();
                    wp.CSpeed         = r.ReadDouble();
                    wp.LtrTime        = r.ReadDouble();
                    wp.LtrRadius      = r.ReadDouble();
                    wp.LtrSpeed       = r.ReadDouble();
                    wp.LtrAlt         = r.ReadDouble();
                    wp.SearchBearing  = r.ReadDouble();
                    wp.SearchWidth    = r.ReadDouble();
                    wp.SearchLength   = r.ReadDouble();
                    wp.SearchTime     = r.ReadDouble();
                    wp.SearchSpeed    = r.ReadDouble();
                    wp.SearchAlt      = r.ReadDouble();
                    wp.POILat         = r.ReadDouble();
                    wp.POILon         = r.ReadDouble();
                    wp.POIAlt         = r.ReadDouble();
                    r.BaseStream.Seek(wp_start + 152, SeekOrigin.Begin);
                    md.WayPoints.Add(wp);
                }
                return md;
            }
        }

        public static void WriteMfp(string path, MissionData md)
        {
            using (var ms = new MemoryStream())
            using (var w  = new BinaryWriter(ms, Encoding.ASCII))
            {
                // Header (128 bytes)
                w.Write(MAGIC);                                         // 0
                w.Write(FORMAT_VERSION);                                // 4
                w.Write((byte)md.UAV_ID);                               // 5
                w.Write((byte)Math.Min(md.WayPoints.Count, 255));       // 6
                byte flags = 0;
                if (md.HasRunwayData)  flags |= 0x01;
                if (md.HasLandingData) flags |= 0x02;
                w.Write(flags);                                         // 7
                WriteFixedString(w, md.Name,       64);                 // 8
                WriteFixedString(w, md.CreatedBy,  16);                 // 72
                w.Write((uint)new DateTimeOffset(md.CreateDate).ToUnixTimeSeconds()); // 88
                w.Write(new byte[36]);                                  // 92 reserved

                // GCS (24 bytes) @ 128
                WriteLLA(w, md.GCS);

                // Takeoff (112 bytes) @ 152
                WriteLLA(w, md.TORunwayEnd1);
                WriteLLA(w, md.TORunwayEnd2);
                WriteLLA(w, md.ClimbOutEnd1);
                WriteLLA(w, md.ClimbOutEnd2);
                w.Write(md.TORunwayWidth);
                w.Write(md.AbortTaxiDist);

                // Landing (104 bytes) @ 264
                WriteLLA(w, md.LandRunwayEnd1);
                WriteLLA(w, md.LandRunwayEnd2);
                WriteLLA(w, md.LAPoint1);
                WriteLLA(w, md.LAPoint2);
                w.Write(md.LandRunwayWidth);

                // Waypoints @ 368
                foreach (var wp in md.WayPoints)
                {
                    long start = ms.Position;
                    w.Write((byte)(wp.DataValid ? 1 : 0));
                    w.Write((byte)wp.Category);
                    w.Write(wp.Number);
                    w.Write((byte)(wp.LtrFlag          ? 1 : 0));
                    w.Write((byte)(wp.SearchFlag        ? 1 : 0));
                    w.Write((byte)(wp.POIFlag           ? 1 : 0));
                    w.Write((byte)(wp.RadioSilenceFlag  ? 1 : 0));
                    w.Write(wp.Lat);
                    w.Write(wp.Lon);
                    w.Write(wp.Alt);
                    w.Write(wp.CSpeed);
                    w.Write(wp.LtrTime);
                    w.Write(wp.LtrRadius);
                    w.Write(wp.LtrSpeed);
                    w.Write(wp.LtrAlt);
                    w.Write(wp.SearchBearing);
                    w.Write(wp.SearchWidth);
                    w.Write(wp.SearchLength);
                    w.Write(wp.SearchTime);
                    w.Write(wp.SearchSpeed);
                    w.Write(wp.SearchAlt);
                    w.Write(wp.POILat);
                    w.Write(wp.POILon);
                    w.Write(wp.POIAlt);
                    w.Write(new byte[8]);   // reserved
                    System.Diagnostics.Debug.Assert(ms.Position == start + 152);
                }

                // CRC-32 (4 bytes)
                byte[] body = ms.ToArray();
                uint   crc  = Crc32(body, 0, body.Length);
                w.Write(crc);

                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  .mpr text writer  (output for serial TX)
        // ════════════════════════════════════════════════════════════════
        public static void WriteMpr(string path, MissionData md)
        {
            var sb = new System.Text.StringBuilder();
            string bar = new string('=', 77);

            sb.AppendLine(bar);
            sb.AppendLine("=\t\t\tMISSION PROGRAMMING INPUT FILE\t\t\t    =");
            sb.AppendLine(bar);
            sb.AppendLine();
            sb.AppendLine(bar);
            sb.AppendLine("=\t\t\t\tFILE HEADER\t\t\t\t    =");
            sb.AppendLine(bar);
            sb.AppendLine("[HEADER]");
            sb.AppendLine("MPSV = 1.0.1.7");
            sb.AppendLine("MCSV = 1.0.0.2");
            sb.AppendLine($"MName = {md.Name}");
            sb.AppendLine($"MGName = {md.CreatedBy}");
            sb.AppendLine($"GDate = {md.CreateDate:hh:mm:ss tt dd/MM/yyyy}");
            sb.AppendLine($"UAV ={md.UAV_ID} ");

            // CRC-32 of waypoint content (all WP lines) written as decimal
            string wpContent = BuildWPContent(md);
            uint crc = Crc32(System.Text.Encoding.ASCII.GetBytes(wpContent), 0,
                             System.Text.Encoding.ASCII.GetBytes(wpContent).Length);
            sb.AppendLine($"CRC ={crc} ");

            int mwpCount = 0;
            foreach (var w in md.WayPoints)
                if (w.Category == WPCategory.Waypoint || w.Category == WPCategory.BStation)
                    mwpCount++;
            sb.AppendLine($"WayPoints ={mwpCount} ");
            sb.AppendLine();

            sb.AppendLine(bar);
            sb.AppendLine("=\t\t\t\tWAYPOINT DETAIL\t\t\t\t    =");
            sb.AppendLine(bar);
            sb.AppendLine();
            sb.Append(wpContent);

            sb.AppendLine(bar);
            sb.AppendLine("=\t\t\t\t\tE N D\t\t\t\t    =");
            sb.AppendLine(bar);

            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.ASCII);
        }

        private static string BuildWPContent(MissionData md)
        {
            var sb = new System.Text.StringBuilder();
            var ci = CultureInfo.InvariantCulture;

            foreach (var wp in md.WayPoints)
            {
                string tag;
                switch (wp.Category)
                {
                    case WPCategory.BStation:
                    case WPCategory.Waypoint:    tag = $"WP{wp.Number}";   break;
                    case WPCategory.LandApproach: tag = $"LP{wp.Number}"; break;
                    case WPCategory.TakeoffTaxi:  tag = $"TP{wp.Number}"; break;
                    default: tag = $"WP{wp.Number}"; break;
                }
                sb.AppendLine($"[{tag}]");
                sb.AppendLine($"Lat={wp.Lat.ToString("G7", ci)}");
                sb.AppendLine($"Lon={wp.Lon.ToString("G7", ci)}");
                sb.AppendLine($"Alt={wp.Alt.ToString("G7", ci)}");
                sb.AppendLine($"CSpeed={wp.CSpeed.ToString("G6", ci)}");
                sb.AppendLine($"LtrFlag={wp.LtrFlag}");
                if (wp.LtrFlag || wp.LtrAlt != 0 || wp.LtrRadius != 0)
                {
                    sb.AppendLine($"LtrAlt={wp.LtrAlt.ToString("G6", ci)}");
                    sb.AppendLine($"LtrRadius={wp.LtrRadius.ToString("G6", ci)}");
                    sb.AppendLine($"LtrSpeed={wp.LtrSpeed.ToString("G6", ci)}");
                    sb.AppendLine($"LtrTime={wp.LtrTime.ToString("G6", ci)}");
                }
                sb.AppendLine($"POIFlag={wp.POIFlag}");
                if (wp.POIFlag)
                {
                    sb.AppendLine($"POILat={wp.POILat.ToString("G7", ci)}");
                    sb.AppendLine($"POILon={wp.POILon.ToString("G7", ci)}");
                    sb.AppendLine($"POIAlt={wp.POIAlt.ToString("G6", ci)}");
                }
                sb.AppendLine($"SearchFlag={wp.SearchFlag}");
                if (wp.SearchFlag || wp.SearchWidth != 0)
                {
                    sb.AppendLine($"SearchBearing={wp.SearchBearing.ToString("G6", ci)}");
                    sb.AppendLine($"SearchWidth={wp.SearchWidth.ToString("G6", ci)}");
                    sb.AppendLine($"SearchHeight={wp.SearchLength.ToString("G6", ci)}");
                    sb.AppendLine($"SearchTime={wp.SearchTime.ToString("G6", ci)}");
                    sb.AppendLine($"SearchSpeed={wp.SearchSpeed.ToString("G6", ci)}");
                    sb.AppendLine($"SearchAlt={wp.SearchAlt.ToString("G6", ci)}");
                }
            }
            return sb.ToString();
        }

        // ════════════════════════════════════════════════════════════════
        //  Legacy .mpr text reader
        // ════════════════════════════════════════════════════════════════
        public static MissionData ReadMpr(string path)
        {
            string[] lines = File.ReadAllLines(path);
            var md = new MissionData();
            var wpBlocks = new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

            string currentSection = null;
            var    currentBlock   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("=")) continue;

                // Section header
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (currentSection != null)
                        wpBlocks[currentSection] = currentBlock;
                    currentSection = line.Substring(1, line.Length - 2);
                    currentBlock   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 0 || currentSection == null) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                currentBlock[key] = val;
            }
            if (currentSection != null) wpBlocks[currentSection] = currentBlock;

            // Parse HEADER
            if (wpBlocks.TryGetValue("HEADER", out var hdr))
            {
                md.Name      = GV(hdr, "MName");
                md.CreatedBy = GV(hdr, "MGName");
                md.UAV_ID    = (int)GD(hdr, "UAV");
            }

            // Parse waypoints, LP, TP blocks
            var lpRx = new Regex(@"^LP(\d+)$",   RegexOptions.IgnoreCase);
            var tpRx = new Regex(@"^TP(\d+)$",   RegexOptions.IgnoreCase);
            var wpRx = new Regex(@"^WP(\d+)$",   RegexOptions.IgnoreCase);

            foreach (var kvp in wpBlocks)
            {
                string sec  = kvp.Key;
                var    blk  = kvp.Value;
                Match  m;

                if ((m = wpRx.Match(sec)).Success)
                {
                    int num = int.Parse(m.Groups[1].Value);
                    var wp  = ParseWPBlock(blk, num);
                    wp.Category = (num == 0) ? WPCategory.BStation : WPCategory.Waypoint;
                    md.WayPoints.Add(wp);
                }
                else if ((m = lpRx.Match(sec)).Success)
                {
                    int num = int.Parse(m.Groups[1].Value);
                    var wp  = ParseWPBlock(blk, num);
                    wp.Category = WPCategory.LandApproach;
                    md.WayPoints.Add(wp);
                }
                else if ((m = tpRx.Match(sec)).Success)
                {
                    int num = int.Parse(m.Groups[1].Value);
                    var wp  = ParseWPBlock(blk, num);
                    wp.Category = WPCategory.TakeoffTaxi;
                    md.WayPoints.Add(wp);
                }
            }

            // Sort: BStation first, then WP by number, then LP, then TP
            md.WayPoints.Sort((a, b) =>
            {
                int catOrder(WPCategory c) =>
                    c == WPCategory.BStation ? 0 :
                    c == WPCategory.Waypoint ? 1 :
                    c == WPCategory.LandApproach ? 2 : 3;
                int co = catOrder(a.Category).CompareTo(catOrder(b.Category));
                return co != 0 ? co : a.Number.CompareTo(b.Number);
            });

            return md;
        }

        private static MissionWP ParseWPBlock(Dictionary<string, string> blk, int num)
        {
            var wp = new MissionWP
            {
                Number     = (ushort)num,
                DataValid  = true,
                Lat        = GD(blk, "Lat"),
                Lon        = GD(blk, "Lon"),
                Alt        = GD(blk, "Alt"),
                CSpeed     = GD(blk, "CSpeed"),
                LtrFlag    = GB(blk, "LtrFlag"),
                LtrAlt     = GD(blk, "LtrAlt"),
                LtrRadius  = GD(blk, "LtrRadius"),
                LtrSpeed   = GD(blk, "LtrSpeed"),
                LtrTime    = GD(blk, "LtrTime"),
                SearchFlag    = GB(blk, "SearchFlag"),
                SearchBearing = GD(blk, "SearchBearing"),
                SearchWidth   = GD(blk, "SearchWidth"),
                SearchLength  = GD(blk, "SearchHeight"),  // note: mpr calls it Height
                SearchTime    = GD(blk, "SearchTime"),
                SearchSpeed   = GD(blk, "SearchSpeed"),
                SearchAlt     = GD(blk, "SearchAlt"),
                POIFlag = GB(blk, "POIFlag"),
                POILat  = GD(blk, "POILat"),
                POILon  = GD(blk, "POILon"),
                POIAlt  = GD(blk, "POIAlt"),
            };
            return wp;
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static string GV(Dictionary<string, string> d, string k)
            => d.TryGetValue(k, out string v) ? v : "";

        private static double GD(Dictionary<string, string> d, string k)
        {
            if (!d.TryGetValue(k, out string v)) return 0;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0;
        }

        private static bool GB(Dictionary<string, string> d, string k)
        {
            if (!d.TryGetValue(k, out string v)) return false;
            return v.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                   v.Equals("1",    StringComparison.Ordinal);
        }

        private static LLAPoint ReadLLA(BinaryReader r)
            => new LLAPoint { Lat = r.ReadDouble(), Lon = r.ReadDouble(), Alt = r.ReadDouble() };

        private static void WriteLLA(BinaryWriter w, LLAPoint p)
        { w.Write(p.Lat); w.Write(p.Lon); w.Write(p.Alt); }

        private static string ReadFixedString(BinaryReader r, int size)
        {
            byte[] buf = r.ReadBytes(size);
            int    end = Array.IndexOf(buf, (byte)0);
            return Encoding.ASCII.GetString(buf, 0, end < 0 ? size : end);
        }

        private static void WriteFixedString(BinaryWriter w, string s, int size)
        {
            byte[] buf = new byte[size];
            if (!string.IsNullOrEmpty(s))
            {
                byte[] src = Encoding.ASCII.GetBytes(s);
                Array.Copy(src, buf, Math.Min(src.Length, size - 1));
            }
            w.Write(buf);
        }

        // ── CRC-32 (poly 0xEDB88320, standard) ───────────────────────────
        private static readonly uint[] _crcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public static uint Crc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = offset; i < offset + count; i++)
                crc = _crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFF;
        }
    }
}
