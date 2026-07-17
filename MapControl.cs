using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

// ================================================================
//  MapControl.cs  –  GCS_240626
//
//  Satellite moving-map using Esri World Imagery tiles.
//  Zero NuGet dependencies — standard System.Net.WebClient only.
//
//  Tile source  : Esri World Imagery (free, no API key)
//  Tile cache   : %LocalAppData%\GCS_240626\tiles\sat\{z}\{y}\{x}
//
//  Mission loading
//  ───────────────
//  Click "📂 Mission" to load a .mfp (binary) or .mpr (legacy text) file.
//  Active WP (from telemetry Next7WayPoint7Number) is highlighted cyan.
//  Runway, approach and taxi points are drawn with distinct styles.
//
//  Public interface:
//      UpdatePosition(lat, lon, heading)       – call from display timer
//      SetActiveWaypoint(int wpNum)            – call from display timer
//      CacheArea(lat, lon, radiusKm, min, max) – pre-fetch for offline
// ================================================================

namespace GCS_240626
{
    public class MapControl : Panel
    {
        // ── Public state ─────────────────────────────────────────────────
        public double Lat     { get; private set; }
        public double Lon     { get; private set; }
        public double Heading { get; private set; }

        // ── Map state ─────────────────────────────────────────────────────
        private double _centerLat, _centerLon;
        private int    _zoom = 16;
        private bool   _posValid;
        private bool   _homeSet;
        private double _homeLat, _homeLon;
        private bool   _followAircraft = true;

        // ── Track history ─────────────────────────────────────────────────
        private const int    TRACK_MAX  = 500;
        private const double MIN_DIST_M = 2.0;
        private readonly List<(double Lat, double Lon)> _track =
            new List<(double, double)>(520);
        private double _lastLat, _lastLon;
        private bool   _hasLast;

        // ── Mission data ──────────────────────────────────────────────────
        /// <summary>Fixed mission file saved after every successful load — used for auto-load on startup.</summary>
        public static readonly string FixedMissionPath =
            Path.Combine(Application.StartupPath, "mission.mfp");

        private MissionData _mission;           // loaded file — used for upload only
        private MissionData _displayMission;    // drawn on map immediately after load
        private byte        _missionVehicleId;
        private int         _activeWpNum = -1;

        // ── Last-upload center persistence ────────────────────────────────
        private static readonly string ConfigFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GCS_240626", "last_center.cfg");

        private static void SaveCenter(double lat, double lon)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile));
                File.WriteAllLines(ConfigFile, new[] { lat.ToString("R"), lon.ToString("R") });
            }
            catch { }
        }

        private static bool TryLoadCenter(out double lat, out double lon)
        {
            lat = lon = 0;
            try
            {
                if (!File.Exists(ConfigFile)) return false;
                string[] lines = File.ReadAllLines(ConfigFile);
                return lines.Length >= 2
                    && double.TryParse(lines[0],
                           System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out lat)
                    && double.TryParse(lines[1],
                           System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out lon);
            }
            catch { return false; }
        }

        // ── Tile constants ────────────────────────────────────────────────
        private const int    TILE_SIZE  = 256;
        private const string USER_AGENT = "GCS_240626/1.0 (UAV ground station)";
        // Esri World Imagery: note URL is {z}/{y}/{x} (y and x swapped vs OSM)
        private const string TILE_URL   =
            "https://server.arcgisonline.com/ArcGIS/rest/services/" +
            "World_Imagery/MapServer/tile/{0}/{1}/{2}";

        private static readonly string CacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GCS_240626", "tiles", "sat");

        // ── In-memory tile bitmap cache ───────────────────────────────────
        private struct TileKey
        {
            public int X, Y, Z;
            public override bool Equals(object o)
                => o is TileKey k && k.X == X && k.Y == Y && k.Z == Z;
            public override int GetHashCode()
                => X * 1000003 ^ Y * 997 ^ Z * 31;
        }
        private readonly Dictionary<TileKey, Bitmap> _bmpCache =
            new Dictionary<TileKey, Bitmap>();
        private readonly HashSet<TileKey>    _pending   = new HashSet<TileKey>();
        private readonly object              _cacheLock = new object();
        private readonly SemaphoreSlim       _dlSem     = new SemaphoreSlim(4, 4);

        // ── UAV icon (PNG loaded from exe directory) ──────────────────────
        // Replace uav_icon.png in the application folder to change the icon.
        private Bitmap _uavIcon;
        private const string ICON_FILENAME = "uav_icon.png";
        private const int    ICON_DRAW_PX  = 56;   // rendered size on map (pixels)

        // ── Fonts ────────────────────────────────────────────────────────
        private readonly Font _fntS  = new Font("Consolas",  8.5f, FontStyle.Bold);
        private readonly Font _fntWP = new Font("Consolas",  8.0f, FontStyle.Bold);

        // ── Mission send event ────────────────────────────────────────────
        /// <summary>Fired when "⬆ Send" clicked. Form1 calls MissionUploader.UploadAsync.</summary>
        public event Action<MissionData, byte> MissionSendRequested;

        // ── Overlay controls ──────────────────────────────────────────────
        private readonly Label  _statusLbl;

        // ── Mission buttons moved to Form1 — public API ───────────────────
        private bool _showSendWindow = false;
        /// <summary>Set by Form1's Send log checkbox.</summary>
        public bool ShowSendWindow { get => _showSendWindow; set => _showSendWindow = value; }

        /// <summary>Fired when a mission is loaded (true) — enables the Send button in Form1.</summary>
        public event Action<bool> SendEnabledChanged;

        /// <summary>Fired when tile caching starts (false) or finishes (true) — controls Cache button.</summary>
        public event Action<bool> CacheEnabledChanged;

        public void InvokeLoadMission() => OnLoadMissionClick(null, EventArgs.Empty);
        public void InvokeSendMission() => OnSendMissionClick(null, EventArgs.Empty);
        public void InvokeCacheArea()   => OnCacheBtnClick(null, EventArgs.Empty);

        // ── Pan state ─────────────────────────────────────────────────────
        private bool   _panning;
        private Point  _panStart;
        private double _panStartLat, _panStartLon;

        // ════════════════════════════════════════════════════════════════
        //  Constructor
        // ════════════════════════════════════════════════════════════════
        public MapControl()
        {
            DoubleBuffered = true;
            BackColor      = Color.FromArgb(18, 18, 18);
            BorderStyle    = BorderStyle.None;

            Directory.CreateDirectory(CacheRoot);

            // Restore map center from last successful upload (no waypoints yet)
            if (TryLoadCenter(out double savedLat, out double savedLon))
            {
                _centerLat = savedLat;
                _centerLon = savedLon;
            }

            MouseWheel += OnMouseWheel;
            MouseDown  += OnMouseDown;
            MouseMove  += OnMouseMove;
            MouseUp    += OnMouseUp;

            _statusLbl = new Label
            {
                Text      = "Waiting for GPS fix…",
                AutoSize  = false,
                Size      = new Size(700, 20),
                Location  = new Point(470, 9),   // original position — row 1, after buttons
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                Font      = new Font("Consolas", 8f),
            };

            Controls.Add(_statusLbl);
            _statusLbl.BringToFront();

            LoadUavIcon();
        }

        // ════════════════════════════════════════════════════════════════
        //  Public API
        // ════════════════════════════════════════════════════════════════
        public void UpdatePosition(double lat, double lon, double heading)
        {
            if (lat == 0.0 && lon == 0.0) return;

            Lat = lat;  Lon = lon;  Heading = heading;
            _posValid = true;
            _statusLbl.Text = $"  {lat:F5}°N   {lon:F5}°E   Hdg {heading:F0}°";

            if (!_homeSet) { _homeLat = lat; _homeLon = lon; _homeSet = true; }

            if (!_hasLast || DistM(lat, lon, _lastLat, _lastLon) >= MIN_DIST_M)
            {
                if (_track.Count >= TRACK_MAX) _track.RemoveAt(0);
                _track.Add((lat, lon));
                _lastLat = lat;  _lastLon = lon;  _hasLast = true;
            }

            if (_followAircraft) { _centerLat = lat; _centerLon = lon; }
            Invalidate();
        }

        /// <summary>Called by Form1 during mission upload to show TX progress.</summary>
        public void ReportUploadStatus(string msg) => SetStatus(msg);


        /// <summary>Call each display cycle with Next7WayPoint7Number from telemetry.</summary>
        public void SetActiveWaypoint(int wpNum)
        {
            if (_activeWpNum == wpNum) return;
            _activeWpNum = wpNum;
            Invalidate();
        }

        /// <summary>
        /// Called by Form1 after a successful mission upload.
        /// Enables waypoint display and saves the center for next launch.
        /// </summary>
        public void OnMissionUploaded(MissionData md)
        {
            if (md == null) return;
            _displayMission = md;

            if (md.WayPoints.Count > 0)
            {
                _centerLat      = md.WayPoints[0].Lat;
                _centerLon      = md.WayPoints[0].Lon;
                _followAircraft = false;
                SaveCenter(_centerLat, _centerLon);
            }
            Invalidate();
        }

        /// <summary>Called by Form1 after a successful mission download to display it on the map.</summary>
        public void LoadDownloadedMission(MissionData md)
        {
            if (md == null) return;
            _mission          = md;
            _missionVehicleId = md.UAV_ID >= 0 && md.UAV_ID <= 255 ? (byte)md.UAV_ID : (byte)0;
            SendEnabledChanged?.Invoke(true);

            int wpCount = 0;
            foreach (var w in md.WayPoints)
                if (w.Category == WPCategory.Waypoint || w.Category == WPCategory.BStation)
                    wpCount++;

            SetStatus($"Downloaded '{md.Name}'  {wpCount} WPs  — press ⬆ Send to re-upload");

            if (md.WayPoints.Count > 0)
            {
                _centerLat = md.WayPoints[0].Lat;
                _centerLon = md.WayPoints[0].Lon;
            }
            Invalidate();
        }

        // ════════════════════════════════════════════════════════════════
        //  Paint
        // ════════════════════════════════════════════════════════════════
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cx = Width  / 2;
            int cy = Height / 2;

            DrawTiles(g, cx, cy);
            DrawZoomLabel(g);
            DrawMission(g, cx, cy);

            if (!_posValid) return;
            DrawTrack(g, cx, cy);
            DrawHomeMarker(g, cx, cy);
            DrawAircraft(g, cx, cy);
        }

        // ── Tile layer ─────────────────────────────────────────────────────
        private void DrawTiles(Graphics g, int cx, int cy)
        {
            double ftx = LonToTileF(_centerLon, _zoom);
            double fty = LatToTileF(_centerLat, _zoom);
            int    ctx = (int)Math.Floor(ftx);
            int    cty = (int)Math.Floor(fty);
            int    ox  = (int)Math.Round((ftx - ctx) * TILE_SIZE);
            int    oy  = (int)Math.Round((fty - cty) * TILE_SIZE);

            int tilesX = Width  / TILE_SIZE / 2 + 2;
            int tilesY = Height / TILE_SIZE / 2 + 2;
            int maxT   = (1 << _zoom) - 1;

            for (int dy = -tilesY; dy <= tilesY; dy++)
            for (int dx = -tilesX; dx <= tilesX; dx++)
            {
                int tx = ctx + dx;
                int ty = cty + dy;
                if (tx < 0 || ty < 0 || tx > maxT || ty > maxT) continue;

                int px = cx - ox + dx * TILE_SIZE;
                int py = cy - oy + dy * TILE_SIZE;
                if (px + TILE_SIZE < 0 || px > Width ||
                    py + TILE_SIZE < 0 || py > Height) continue;

                var key = new TileKey { X = tx, Y = ty, Z = _zoom };
                Bitmap bmp = null;
                lock (_cacheLock) _bmpCache.TryGetValue(key, out bmp);

                if (bmp != null)
                    g.DrawImage(bmp, px, py, TILE_SIZE, TILE_SIZE);
                else
                {
                    g.FillRectangle(Brushes.DimGray, px + 1, py + 1,
                                    TILE_SIZE - 2, TILE_SIZE - 2);
                    ScheduleLoad(key);
                }
            }
        }

        // ── Complete mission overlay ──────────────────────────────────────
        private void DrawMission(Graphics g, int cx, int cy)
        {
            if (_displayMission == null) return;

            // ── Runway line ──────────────────────────────────────────────
            if (_displayMission.HasRunwayData)
            {
                PointF rw1 = ToScreen(_displayMission.TORunwayEnd1.Lat, _displayMission.TORunwayEnd1.Lon, cx, cy);
                PointF rw2 = ToScreen(_displayMission.TORunwayEnd2.Lat, _displayMission.TORunwayEnd2.Lon, cx, cy);
                using (var pen = new Pen(Color.White, 3f))
                    g.DrawLine(pen, rw1, rw2);
                DrawSmallLabel(g, "04", rw1, Color.White);
                DrawSmallLabel(g, "22", rw2, Color.White);
            }

            // ── Approach lines ───────────────────────────────────────────
            if (_displayMission.HasApproachData && _displayMission.HasLandingData)
            {
                using (var pen = new Pen(Color.FromArgb(180, 80, 160, 255), 1.5f)
                       { DashStyle = DashStyle.Dash })
                {
                    PointF la1 = ToScreen(_displayMission.LAPoint1.Lat, _displayMission.LAPoint1.Lon, cx, cy);
                    PointF le1 = ToScreen(_displayMission.LandRunwayEnd1.Lat, _displayMission.LandRunwayEnd1.Lon, cx, cy);
                    g.DrawLine(pen, la1, le1);

                    PointF la2 = ToScreen(_displayMission.LAPoint2.Lat, _displayMission.LAPoint2.Lon, cx, cy);
                    PointF le2 = ToScreen(_displayMission.LandRunwayEnd2.Lat, _displayMission.LandRunwayEnd2.Lon, cx, cy);
                    g.DrawLine(pen, la2, le2);
                }
            }

            // ── Mission WP route line (WP0 → WP1 → … → WPn) ────────────
            var mwps = new List<MissionWP>(_displayMission.WayPoints.Count);
            foreach (var w in _displayMission.WayPoints)
                if (w.Category == WPCategory.Waypoint || w.Category == WPCategory.BStation)
                    mwps.Add(w);

            if (mwps.Count >= 2)
            {
                using (var pen = new Pen(Color.FromArgb(180, 8, 129, 196), 1.5f)
                       { DashStyle = DashStyle.Dash })
                {
                    for (int i = 1; i < mwps.Count; i++)
                    {
                        PointF p0 = ToScreen(mwps[i-1].Lat, mwps[i-1].Lon, cx, cy);
                        PointF p1 = ToScreen(mwps[i  ].Lat, mwps[i  ].Lon, cx, cy);
                        g.DrawLine(pen, p0, p1);
                    }
                }
            }

            // ── Draw all WPs by category ─────────────────────────────────
            foreach (var wp in _displayMission.WayPoints)
            {
                PointF pt = ToScreen(wp.Lat, wp.Lon, cx, cy);
                if (pt.X < -40 || pt.X > Width + 40 ||
                    pt.Y < -40 || pt.Y > Height + 40) continue;

                switch (wp.Category)
                {
                    case WPCategory.BStation:
                        DrawBStationMarker(g, pt, wp.Number);
                        break;
                    case WPCategory.Waypoint:
                        DrawWPMarker(g, pt, wp.Number, wp.Number == _activeWpNum);
                        break;
                    case WPCategory.LandApproach:
                        DrawLPMarker(g, pt, wp.Number);
                        break;
                    case WPCategory.TakeoffTaxi:
                        DrawTKMarker(g, pt, wp.Number);
                        break;
                }
            }
        }

        // ── BStation (home/GCS) ──────────────────────────────────────────
        private void DrawBStationMarker(Graphics g, PointF pt, int num)
        {
            using (var pen = new Pen(Color.Lime, 2.5f))
            {
                g.DrawLine(pen, pt.X - 11, pt.Y, pt.X + 11, pt.Y);
                g.DrawLine(pen, pt.X, pt.Y - 11, pt.X, pt.Y + 11);
                g.DrawEllipse(pen, pt.X - 7, pt.Y - 7, 14, 14);
            }
            g.DrawString("GCS", _fntWP, Brushes.Lime, pt.X + 12, pt.Y - 8);
        }

        // ── Normal mission WP ────────────────────────────────────────────
        private void DrawWPMarker(Graphics g, PointF pt, int num, bool isActive)
        {
            Color ring = isActive ? Color.Cyan  : Color.FromArgb(8, 129, 196);
            Color fill = isActive ? Color.FromArgb(80, Color.Cyan)
                                  : Color.FromArgb(60, 8, 129, 196);
            using (var br = new SolidBrush(fill))
                g.FillEllipse(br, pt.X - 9, pt.Y - 9, 18, 18);
            using (var pen = new Pen(ring, isActive ? 2.5f : 1.5f))
                g.DrawEllipse(pen, pt.X - 9, pt.Y - 9, 18, 18);
            string lbl = num.ToString();
            SizeF  sz  = g.MeasureString(lbl, _fntWP);
            using (var br = new SolidBrush(isActive ? Color.Cyan : Color.White))
                g.DrawString(lbl, _fntWP, br, pt.X - sz.Width / 2, pt.Y - sz.Height / 2);
        }

        // ── Landing approach point ────────────────────────────────────────
        private void DrawLPMarker(Graphics g, PointF pt, int num)
        {
            Color c = Color.FromArgb(120, 140, 255);  // blue-violet
            using (var br = new SolidBrush(Color.FromArgb(60, c)))
                g.FillEllipse(br, pt.X - 8, pt.Y - 8, 16, 16);
            using (var pen = new Pen(c, 1.5f))
                g.DrawEllipse(pen, pt.X - 8, pt.Y - 8, 16, 16);
            g.DrawString("LP", _fntWP, new SolidBrush(c), pt.X + 10, pt.Y - 8);
        }

        // ── Takeoff / taxi point ──────────────────────────────────────────
        private void DrawTKMarker(Graphics g, PointF pt, int num)
        {
            Color c = Color.Orange;
            PointF[] tri =
            {
                new PointF(pt.X,      pt.Y - 10),
                new PointF(pt.X + 9,  pt.Y + 6),
                new PointF(pt.X - 9,  pt.Y + 6),
            };
            using (var br = new SolidBrush(Color.FromArgb(60, c)))
                g.FillPolygon(br, tri);
            using (var pen = new Pen(c, 1.5f))
                g.DrawPolygon(pen, tri);
            g.DrawString("TK", _fntWP, new SolidBrush(c), pt.X + 10, pt.Y - 8);
        }

        private void DrawSmallLabel(Graphics g, string text, PointF pt, Color c)
            => g.DrawString(text, _fntWP, new SolidBrush(c), pt.X + 5, pt.Y + 5);

        // ── Track ─────────────────────────────────────────────────────────
        private void DrawTrack(Graphics g, int cx, int cy)
        {
            if (_track.Count < 2) return;
            using (var pen = new Pen(Color.FromArgb(0, 220, 0), 2))
            {
                for (int i = 1; i < _track.Count; i++)
                {
                    PointF p0 = ToScreen(_track[i-1].Lat, _track[i-1].Lon, cx, cy);
                    PointF p1 = ToScreen(_track[i  ].Lat, _track[i  ].Lon, cx, cy);
                    g.DrawLine(pen, p0, p1);
                }
            }
        }

        // ── HOME marker ───────────────────────────────────────────────────
        private void DrawHomeMarker(Graphics g, int cx, int cy)
        {
            if (!_homeSet) return;
            PointF pt = ToScreen(_homeLat, _homeLon, cx, cy);
            using (var pen = new Pen(Color.Yellow, 2))
            {
                g.DrawLine(pen, pt.X - 9, pt.Y,     pt.X + 9, pt.Y);
                g.DrawLine(pen, pt.X,     pt.Y - 9, pt.X,     pt.Y + 9);
                g.DrawEllipse(pen, pt.X - 5, pt.Y - 5, 10, 10);
            }
            g.DrawString("HOME", _fntS, Brushes.Yellow, pt.X + 10, pt.Y - 9);
        }

        // ── Icon loader ───────────────────────────────────────────────────
        /// <summary>
        /// Loads uav_icon.png from the same folder as the executable.
        /// Replace that file to use a different UAV silhouette.
        /// The PNG should be square, RGBA, nose pointing UP (−Y), centred.
        /// </summary>
        private void LoadUavIcon()
        {
            try
            {
                string path = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath) ?? ".",
                    ICON_FILENAME);
                if (File.Exists(path))
                    _uavIcon = new Bitmap(path);
            }
            catch { _uavIcon = null; }
        }

        // ── Aircraft symbol ───────────────────────────────────────────────
        // Rotated by Heading (0° = nose north = up on screen).
        private void DrawAircraft(Graphics g, int cx, int cy)
        {
            PointF pos = ToScreen(Lat, Lon, cx, cy);   // true screen position
            var state = g.Save();
            g.TranslateTransform(pos.X, pos.Y);
            g.RotateTransform((float)Heading);

            if (_uavIcon != null)
            {
                // Draw PNG icon centred on aircraft position, fixed pixel size.
                int half = ICON_DRAW_PX / 2;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(_uavIcon,
                    new Rectangle(-half, -half, ICON_DRAW_PX, ICON_DRAW_PX));
            }
            else
            {
                // Fallback procedural drawing (used if uav_icon.png is missing).
                DrawAircraftProcedural(g);
            }

            g.Restore(state);
        }

        /// <summary>Procedural UAV silhouette — used when uav_icon.png is absent.</summary>
        private static void DrawAircraftProcedural(Graphics g)
        {
            PointF[] wings =
            {
                new PointF( -2f,  -2f), new PointF(  2f,  -2f),
                new PointF( 21f,   5f), new PointF( 19f,   9f),
                new PointF(  2f,   5f), new PointF( -2f,   5f),
                new PointF(-19f,   9f), new PointF(-21f,   5f),
            };
            PointF[] hTail =
            {
                new PointF(  0f, 12f), new PointF(  9f, 15f),
                new PointF(  8f, 19f), new PointF(  0f, 17f),
                new PointF( -8f, 19f), new PointF( -9f, 15f),
            };
            PointF[] fuse =
            {
                new PointF(  0f, -19f), new PointF(  2f, -10f),
                new PointF(  2f,   6f), new PointF(1.5f,  12f),
                new PointF(  0f,  20f), new PointF(-1.5f, 12f),
                new PointF( -2f,   6f), new PointF( -2f, -10f),
            };

            using (var wFill = new SolidBrush(Color.FromArgb(210, 0, 200, 0)))
            using (var tFill = new SolidBrush(Color.FromArgb(210, 0, 175, 0)))
            using (var bdr   = new Pen(Color.FromArgb(200, 255, 255, 255), 1f))
            using (var bdrF  = new Pen(Color.White, 1.5f))
            {
                g.FillPolygon(wFill, wings); g.DrawPolygon(bdr, wings);
                g.FillPolygon(tFill, hTail); g.DrawPolygon(bdr, hTail);
                g.FillPolygon(Brushes.Lime,  fuse);  g.DrawPolygon(bdrF, fuse);
            }
            g.FillEllipse(Brushes.White, -1.5f, -14f, 3f, 3f);
        }

        // ── Zoom label ────────────────────────────────────────────────────
        private void DrawZoomLabel(Graphics g)
            => g.DrawString($"Z{_zoom}", _fntS, Brushes.White, Width - 32, 8);

        // ════════════════════════════════════════════════════════════════
        //  Mission file loading — load into memory, enable Send
        // ════════════════════════════════════════════════════════════════
        private void OnLoadMissionClick(object sender, EventArgs e)
        {
            // Pick mission file
            string srcPath;
            using (var dlg = new OpenFileDialog
            {
                Title  = "Load Mission File",
                Filter = "Mission files (*.mfp;*.mpr)|*.mfp;*.mpr|All files (*.*)|*.*",
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                srcPath = dlg.FileName;
            }

            // Parse
            MissionData md;
            try   { md = MissionFile.Read(srcPath); }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load mission file:\n{ex.Message}",
                    "Load Mission", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (md.WayPoints.Count == 0)
            {
                MessageBox.Show("No waypoints found in mission file.",
                    "Load Mission", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Store in memory — no save dialogs
            _missionVehicleId = md.UAV_ID >= 0 && md.UAV_ID <= 255 ? (byte)md.UAV_ID : (byte)0;
            _mission        = md;
            _displayMission = md;   // show waypoints on map immediately
            SendEnabledChanged?.Invoke(true);

            int wpCount = 0;
            foreach (var w in md.WayPoints)
                if (w.Category == WPCategory.Waypoint || w.Category == WPCategory.BStation)
                    wpCount++;

            SetStatus($"Loaded '{md.Name}'  {wpCount} WPs  [{Path.GetFileName(srcPath)}]  — press ⬆ Send to upload");

            // Center map on WP0, set HOME, clear old trail
            if (_mission.WayPoints.Count > 0)
            {
                _centerLat      = _mission.WayPoints[0].Lat;
                _centerLon      = _mission.WayPoints[0].Lon;
                _homeLat        = _mission.WayPoints[0].Lat;
                _homeLon        = _mission.WayPoints[0].Lon;
                _homeSet        = true;
                _followAircraft = false;
            }
            _track.Clear();
            _hasLast = false;
            Invalidate();

            // Save a fixed-name copy for auto-load on next launch
            try { File.Copy(srcPath, FixedMissionPath, overwrite: true); } catch { }
        }

        // ── Auto-load last mission (no dialog) ───────────────────────────
        public void AutoLoadLastMission(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            MissionData md;
            try   { md = MissionFile.Read(path); }
            catch { return; }

            if (md.WayPoints.Count == 0) return;

            _missionVehicleId = md.UAV_ID >= 0 && md.UAV_ID <= 255 ? (byte)md.UAV_ID : (byte)0;
            _mission        = md;
            _displayMission = md;   // show waypoints on map immediately
            SendEnabledChanged?.Invoke(true);

            int wpCount = 0;
            foreach (var w in md.WayPoints)
                if (w.Category == WPCategory.Waypoint || w.Category == WPCategory.BStation)
                    wpCount++;

            SetStatus($"Auto-loaded '{md.Name}'  {wpCount} WPs  [{Path.GetFileName(path)}]  — press ⬆ Send to upload");

            if (md.WayPoints.Count > 0)
            {
                _centerLat      = md.WayPoints[0].Lat;
                _centerLon      = md.WayPoints[0].Lon;
                _homeLat        = md.WayPoints[0].Lat;
                _homeLon        = md.WayPoints[0].Lon;
                _homeSet        = true;
                _followAircraft = false;
            }
            _track.Clear();
            _hasLast = false;
            Invalidate();
        }

        // ── "⬆ Send" button ───────────────────────────────────────────────
        private void OnSendMissionClick(object sender, EventArgs e)
        {
            if (_mission == null) return;
            MissionSendRequested?.Invoke(_mission, _missionVehicleId);
        }

        // ════════════════════════════════════════════════════════════════
        //  Tile fetch pipeline
        // ════════════════════════════════════════════════════════════════
        private void ScheduleLoad(TileKey key)
        {
            lock (_cacheLock)
            {
                if (_pending.Contains(key)) return;
                _pending.Add(key);
            }

            Task.Run(async () =>
            {
                await _dlSem.WaitAsync().ConfigureAwait(false);
                try
                {
                    Bitmap bmp = LoadFromDisk(key) ?? await DownloadAsync(key);
                    if (bmp != null)
                    {
                        lock (_cacheLock)
                        {
                            _bmpCache[key] = bmp;
                            _pending.Remove(key);
                        }
                        if (!IsDisposed)
                            BeginInvoke(new Action(Invalidate));
                    }
                    else
                        lock (_cacheLock) _pending.Remove(key);
                }
                finally { _dlSem.Release(); }
            });
        }

        private static Bitmap LoadFromDisk(TileKey k)
        {
            string path = TilePath(k);
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                return new Bitmap(new MemoryStream(bytes));
            }
            catch { return null; }
        }

        private static async Task<Bitmap> DownloadAsync(TileKey k)
        {
            // Esri tile URL: {z}/{y}/{x}  (y and x swapped vs OSM convention)
            string url = string.Format(TILE_URL, k.Z, k.Y, k.X);
            try
            {
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = USER_AGENT;
                    byte[] bytes = await wc.DownloadDataTaskAsync(url).ConfigureAwait(false);
                    SaveToDisk(k, bytes);
                    return new Bitmap(new MemoryStream(bytes));
                }
            }
            catch { return null; }
        }

        private static void SaveToDisk(TileKey k, byte[] bytes)
        {
            try
            {
                string path = TilePath(k);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, bytes);
            }
            catch { }
        }

        private static string TilePath(TileKey k)
            => Path.Combine(CacheRoot, k.Z.ToString(), k.Y.ToString(), k.X + ".jpg");

        // ════════════════════════════════════════════════════════════════
        //  Tile pre-cache
        // ════════════════════════════════════════════════════════════════
        private void OnCacheBtnClick(object sender, EventArgs e)
        {
            double lat = _posValid ? Lat : _centerLat;
            double lon = _posValid ? Lon : _centerLon;
            using (var dlg = new CacheAreaDialog(lat, lon))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    CacheArea(dlg.CenterLat, dlg.CenterLon,
                              dlg.RadiusKm, dlg.MinZoom, dlg.MaxZoom);
            }
        }

        public void CacheArea(double centerLat, double centerLon,
                              double radiusKm, int minZoom = 10, int maxZoom = 17)
        {
            CacheEnabledChanged?.Invoke(false);
            SetStatus("Preparing tile list…");

            Task.Run(async () =>
            {
                for (int z = minZoom; z <= maxZoom; z++)
                {
                    var tiles = GetTileRange(centerLat, centerLon, radiusKm, z);
                    int total = tiles.Count, done = 0;
                    SetStatus($"Zoom {z}/{maxZoom}: 0/{total}");

                    var sem   = new SemaphoreSlim(4, 4);
                    var tasks = new List<Task>(tiles.Count);
                    foreach (var tile in tiles)
                    {
                        await sem.WaitAsync().ConfigureAwait(false);
                        var t = tile;
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                if (!File.Exists(TilePath(t)))
                                    await DownloadAsync(t).ConfigureAwait(false);
                                int d = Interlocked.Increment(ref done);
                                if (d % 20 == 0 || d == total)
                                    SetStatus($"Zoom {z}/{maxZoom}: {d}/{total}");
                            }
                            finally { sem.Release(); }
                        }));
                    }
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                SetStatus("Cache complete ✓");
                if (!IsDisposed)
                    BeginInvoke(new Action(() => CacheEnabledChanged?.Invoke(true)));
            });
        }

        private static List<TileKey> GetTileRange(
            double lat, double lon, double radiusKm, int zoom)
        {
            double latD = radiusKm / 111.0;
            double lonD = radiusKm / (111.0 * Math.Cos(lat * Math.PI / 180.0));
            int x0 = (int)Math.Floor(LonToTileF(lon - lonD, zoom));
            int x1 = (int)Math.Floor(LonToTileF(lon + lonD, zoom));
            int y0 = (int)Math.Floor(LatToTileF(lat + latD, zoom));
            int y1 = (int)Math.Floor(LatToTileF(lat - latD, zoom));
            int max = (1 << zoom) - 1;
            var list = new List<TileKey>();
            for (int x = Math.Max(0, x0); x <= Math.Min(max, x1); x++)
            for (int y = Math.Max(0, y0); y <= Math.Min(max, y1); y++)
                list.Add(new TileKey { X = x, Y = y, Z = zoom });
            return list;
        }

        // ════════════════════════════════════════════════════════════════
        //  Coordinate helpers
        // ════════════════════════════════════════════════════════════════
        private PointF ToScreen(double lat, double lon, int cx, int cy)
        {
            double dx = (LonToTileF(lon, _zoom) - LonToTileF(_centerLon, _zoom)) * TILE_SIZE;
            double dy = (LatToTileF(lat, _zoom) - LatToTileF(_centerLat, _zoom)) * TILE_SIZE;
            return new PointF((float)(cx + dx), (float)(cy + dy));
        }

        private static double LonToTileF(double lon, int z)
            => (lon + 180.0) / 360.0 * (1 << z);

        private static double LatToTileF(double lat, int z)
        {
            double r = lat * Math.PI / 180.0;
            return (1.0 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI)
                   / 2.0 * (1 << z);
        }

        private static double TileToLat(double ty, int z)
        {
            double n = Math.PI - 2.0 * Math.PI * ty / (1 << z);
            return 180.0 / Math.PI * Math.Atan((Math.Exp(n) - Math.Exp(-n)) / 2.0);
        }

        private static double DistM(double lat1, double lon1, double lat2, double lon2)
        {
            double dN = (lat1 - lat2) * 111000.0;
            double dE = (lon1 - lon2) * 111000.0 * Math.Cos(lat1 * Math.PI / 180.0);
            return Math.Sqrt(dN * dN + dE * dE);
        }

        // ════════════════════════════════════════════════════════════════
        //  Mouse: wheel zoom + left-drag pan
        // ════════════════════════════════════════════════════════════════
        private void OnMouseWheel(object s, MouseEventArgs e)
        {
            _zoom = Math.Max(2, Math.Min(19, _zoom + (e.Delta > 0 ? 1 : -1)));
            lock (_cacheLock) { _bmpCache.Clear(); _pending.Clear(); }
            Invalidate();
        }

        private void OnMouseDown(object s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _panning = true;  _panStart = e.Location;
            _panStartLat = _centerLat;  _panStartLon = _centerLon;
            _followAircraft = false;
        }

        private void OnMouseMove(object s, MouseEventArgs e)
        {
            if (!_panning) return;
            double tpp  = 1.0 / TILE_SIZE;
            _centerLon  = _panStartLon - (e.X - _panStart.X) * 360.0 * tpp / (1 << _zoom);
            double fty  = LatToTileF(_panStartLat, _zoom) - (e.Y - _panStart.Y) * tpp;
            _centerLat  = TileToLat(fty, _zoom);
            Invalidate();
        }

        private void OnMouseUp(object s, MouseEventArgs e) => _panning = false;

        // ── Helpers ───────────────────────────────────────────────────────
        private void SetStatus(string msg)
        {
            if (_statusLbl.InvokeRequired)
                _statusLbl.BeginInvoke(new Action(() => _statusLbl.Text = msg));
            else
                _statusLbl.Text = msg;
        }

        private Button MakeBtn(string text, int x, int y, Color fg, Color bg)
        {
            var btn = new Button
            {
                Text      = text,
                Size      = new Size(90, 26),
                Location  = new Point(x, y),
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Consolas", 8f, FontStyle.Bold),
            };
            btn.FlatAppearance.BorderColor = fg;
            Controls.Add(btn);
            return btn;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_cacheLock)
                {
                    foreach (var bmp in _bmpCache.Values) bmp?.Dispose();
                    _bmpCache.Clear();
                }
                _dlSem.Dispose();
                _fntS.Dispose();
                _fntWP.Dispose();
                _uavIcon?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Cache area dialog
    // ════════════════════════════════════════════════════════════════
    internal class CacheAreaDialog : Form
    {
        public double CenterLat { get; private set; }
        public double CenterLon { get; private set; }
        public double RadiusKm  { get; private set; }
        public int    MinZoom   { get; private set; }
        public int    MaxZoom   { get; private set; }

        private readonly NumericUpDown _lat, _lon, _radius, _zMin, _zMax;

        public CacheAreaDialog(double lat, double lon)
        {
            Text            = "Pre-Cache Satellite Tiles";
            Size            = new Size(340, 272);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = false;
            MinimizeBox     = false;
            BackColor       = Color.FromArgb(30, 30, 30);
            ForeColor       = Color.White;
            Font            = new Font("Consolas", 9f);

            int y = 14;
            _lat    = Row("Centre lat:",   lat,   -90,  90, 6, ref y);
            _lon    = Row("Centre lon:",   lon,  -180, 180, 6, ref y);
            _radius = Row("Radius (km):",    5,     1, 200, 1, ref y);
            _zMin   = Row("Min zoom:",      10,     2,  19, 0, ref y);
            _zMax   = Row("Max zoom:",      17,     2,  19, 0, ref y);

            var ok = new Button
            {
                Text         = "Start Caching",
                DialogResult = DialogResult.OK,
                Size         = new Size(120, 28),
                Location     = new Point(105, y + 6),
                BackColor    = Color.FromArgb(0, 80, 0),
                ForeColor    = Color.Lime,
                FlatStyle    = FlatStyle.Flat,
            };
            ok.FlatAppearance.BorderColor = Color.Lime;
            ok.Click += (s, e) =>
            {
                CenterLat = (double)_lat.Value;
                CenterLon = (double)_lon.Value;
                RadiusKm  = (double)_radius.Value;
                MinZoom   = (int)_zMin.Value;
                MaxZoom   = (int)_zMax.Value;
            };
            Controls.Add(ok);
            AcceptButton = ok;
        }

        private NumericUpDown Row(string lbl, double val,
                                  double min, double max,
                                  int dec, ref int y)
        {
            Controls.Add(new Label
            {
                Text      = lbl,
                Location  = new Point(12, y + 3),
                Size      = new Size(130, 20),
                ForeColor = Color.LightGray,
            });
            var nud = new NumericUpDown
            {
                Location      = new Point(150, y),
                Size          = new Size(140, 24),
                Minimum       = (decimal)min,
                Maximum       = (decimal)max,
                Value         = Clamp((decimal)val, (decimal)min, (decimal)max),
                DecimalPlaces = dec,
                Increment     = dec > 0 ? 0.001m : 1m,
                BackColor     = Color.FromArgb(45, 45, 45),
                ForeColor     = Color.White,
            };
            Controls.Add(nud);
            y += 34;
            return nud;
        }

        private static decimal Clamp(decimal v, decimal lo, decimal hi)
            => v < lo ? lo : v > hi ? hi : v;
    }
}
