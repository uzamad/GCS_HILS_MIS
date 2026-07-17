using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RpmIndicator
{
    // ============================================================
    //  RpmIndicator – sweep gauge 0-8000 RPM
    //  Green 0-5000, Yellow 5001-7000, Red 7001-8000.
    //  Property: CurrentRPM (int, 0-8000)
    //  Self-contained – no Utilities.dll dependency.
    // ============================================================

    public class RpmIndicator : Control
    {
        // ── Constants ────────────────────────────────────────────
        const int    CONTROL_WIDTH           = 150;
        const int    MINIMUM_CONTROL_WIDTH   = 150;
        const int    MAXIMUM_CONTROL_WIDTH   = 500;
        const int    CONTROL_WIDTH_INCREMENT = 50;
        const string FONT_FAMILY             = "Microsoft Sans Serif";

        const int   MAX_RPM          = 8000;
        const int   MIN_RPM          = 0;
        const float GAUGE_START_DEG  = 40f;   // degrees from top, clockwise, where 0 RPM is
        const float GAUGE_SWEEP_DEG  = 260f;  // total sweep (0 → 8000)

        // Colour bands (in RPM)
        const int GREEN_END  = 5000;
        const int YELLOW_END = 7000;

        static readonly Color BACKGROUND_COLOR = Color.DimGray;
        static readonly Color INSTRUMENT_BG    = Color.Black;
        static readonly Color FONT_COLOR       = Color.White;

        // ── Fields ───────────────────────────────────────────────
        int   control_width   = CONTROL_WIDTH;
        int   current_rpm     = 0;
        int   base_font_size  = 8;
        int   instrument_offset;
        int   outer_ring_radius;    // tick outer edge
        int   inner_ring_radius;    // tick inner edge (long ticks)
        int   mid_ring_radius;      // tick inner edge (short ticks)
        int   arc_outer_radius;     // coloured band outer edge
        int   arc_inner_radius;     // coloured band inner edge

        Bitmap background_bitmap = null;
        bool   redraw_background = true;

        // ── Constructor ──────────────────────────────────────────
        public RpmIndicator()
        {
            Application.ApplicationExit += (s, e) => cleanup();
            this.Width  = CONTROL_WIDTH;
            this.Height = CONTROL_WIDTH;
            this.SetStyle(ControlStyles.DoubleBuffer |
                          ControlStyles.UserPaint    |
                          ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();
            update_geometry();
        }

        // ── Properties ───────────────────────────────────────────
        [Category("Appearance"),
         Description("Gets/Sets current RPM (0-8000)"),
         DefaultValue(0), Bindable(true)]
        public int CurrentRPM
        {
            get { return current_rpm; }
            set
            {
                current_rpm = Math.Max(MIN_RPM, Math.Min(MAX_RPM, value));
                this.Refresh();
            }
        }

        [Category("Appearance"),
         Description("Width/Height of the control (square)"),
         DefaultValue(200), Bindable(true)]
        public int ControlWidth
        {
            get { return control_width; }
            set
            {
                int v = Math.Max(MINIMUM_CONTROL_WIDTH,
                        Math.Min(MAXIMUM_CONTROL_WIDTH, value));
                v = (v / CONTROL_WIDTH_INCREMENT) * CONTROL_WIDTH_INCREMENT;
                if (v == control_width) return;
                control_width = v;
                update_geometry();
                redraw_background = true;
                this.Invalidate();
            }
        }

        // ── Geometry ─────────────────────────────────────────────
        void update_geometry()
        {
            instrument_offset  = Math.Max(4, control_width / 20);
            outer_ring_radius  = control_width / 2 - (int)Math.Round(1.5f * instrument_offset);
            arc_outer_radius   = outer_ring_radius;
            arc_inner_radius   = outer_ring_radius - (int)Math.Round(instrument_offset * 0.9f);
            inner_ring_radius  = arc_inner_radius - 2;
            mid_ring_radius    = (outer_ring_radius + inner_ring_radius) / 2;
            base_font_size     = Math.Max(6, instrument_offset - 2);
        }

        // ── Background (static face) ──────────────────────────────
        void build_background()
        {
            if (background_bitmap != null) background_bitmap.Dispose();
            background_bitmap = new Bitmap(control_width, control_width);
            int cx = control_width / 2;
            int cy = control_width / 2;

            using (Graphics g = Graphics.FromImage(background_bitmap))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;

                // Outer bezel
                var full = new Rectangle(0, 0, control_width - 1, control_width - 1);
                using (var path = rounded_rect(full, control_width / 10))
                using (var b = new SolidBrush(BACKGROUND_COLOR))
                    g.FillPath(b, path);

                // Instrument face circle
                int off  = (int)Math.Round(control_width / 40.0f);
                int diam = control_width - 1 - 2 * off;
                var face = new Rectangle(off, off, diam, diam);
                using (var b = new SolidBrush(INSTRUMENT_BG))
                    g.FillEllipse(b, face);
                using (var p = new Pen(FONT_COLOR, 2f))
                    g.DrawEllipse(p, face);

                // Coloured arc bands (green starts at 1000, not 0)
                draw_arc_band(g, cx, cy, arc_outer_radius, arc_inner_radius,
                    1000, GREEN_END, Color.Lime);
                draw_arc_band(g, cx, cy, arc_outer_radius, arc_inner_radius,
                    GREEN_END, YELLOW_END, Color.Yellow);
                draw_arc_band(g, cx, cy, arc_outer_radius, arc_inner_radius,
                    YELLOW_END, MAX_RPM, Color.Red);

                // Tick marks and labels
                draw_ticks_and_labels(g, cx, cy);

                // Title
                using (var fnt = new Font(FONT_FAMILY,
                                           Math.Max(6, base_font_size),
                                           FontStyle.Bold, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(FONT_COLOR))
                using (var sf = new StringFormat())
                {
                    sf.Alignment     = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Near;
                    string title = "RPM";
                    SizeF  sz    = g.MeasureString(title, fnt);
                    g.DrawString(title, fnt, b,
                        cx - sz.Width / 2f,
                        cy + (inner_ring_radius * 0.25f));

                    // x1000 sub-label
                    using (var fnt2 = new Font(FONT_FAMILY,
                                                Math.Max(5, base_font_size - 2),
                                                FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        string sub = "x1000";
                        SizeF  sz2 = g.MeasureString(sub, fnt2);
                        g.DrawString(sub, fnt2, b,
                            cx - sz2.Width / 2f,
                            cy + (inner_ring_radius * 0.25f) + sz.Height + 1);
                    }
                }
            }
            redraw_background = false;
        }

        void draw_arc_band(Graphics g, int cx, int cy,
                           int outerR, int innerR,
                           int fromRpm, int toRpm, Color color)
        {
            float startDeg = rpm_to_windows_deg(fromRpm);
            float sweepDeg = rpm_to_windows_deg(toRpm) - startDeg;

            int d_outer = outerR * 2;
            int d_inner = innerR * 2;
            var outer_rect = new Rectangle(cx - outerR, cy - outerR, d_outer, d_outer);
            var inner_rect = new Rectangle(cx - innerR, cy - innerR, d_inner, d_inner);

            var outer_path = new GraphicsPath();
            outer_path.AddEllipse(outer_rect);
            var inner_path = new GraphicsPath();
            inner_path.AddEllipse(inner_rect);

            var outer_region = new Region(outer_path);
            var inner_region = new Region(inner_path);
            outer_region.Exclude(inner_region);

            g.SetClip(outer_region, CombineMode.Intersect);
            using (var b = new SolidBrush(color))
                g.FillPie(b, outer_rect, startDeg, sweepDeg);
            g.ResetClip();

            outer_region.Dispose();
            inner_region.Dispose();
            outer_path.Dispose();
            inner_path.Dispose();
        }

        void draw_ticks_and_labels(Graphics g, int cx, int cy)
        {
            // Major ticks every 1000 RPM with label, minor ticks every 500
            // Label colour matches the arc zone: green ≤5000, yellow ≤7000, red >7000
            using (var pen_major  = new Pen(Color.White, 2f))
            using (var pen_minor  = new Pen(Color.White, 1f))
            using (var fnt        = new Font(FONT_FAMILY,
                                             Math.Max(5, base_font_size - 1),
                                             FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b_green    = new SolidBrush(Color.Lime))    // green arc 1-5; label "0" handled separately
            using (var b_yellow   = new SolidBrush(Color.Yellow))
            using (var b_red      = new SolidBrush(Color.Red))
            using (var sf         = new StringFormat())
            {
                sf.Alignment     = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                for (int rpm = MIN_RPM; rpm <= MAX_RPM; rpm += 500)
                {
                    bool major = (rpm % 1000 == 0);
                    float wdeg = rpm_to_windows_deg(rpm);
                    double rad = deg_2_rad(wdeg);
                    float  cos = (float)Math.Cos(rad);
                    float  sin = (float)Math.Sin(rad);

                    int inner = major ? inner_ring_radius : mid_ring_radius;

                    var pen = major ? pen_major : pen_minor;
                    g.DrawLine(pen,
                        cx + inner * cos,            cy + inner * sin,
                        cx + arc_inner_radius * cos, cy + arc_inner_radius * sin);

                    if (major)
                    {
                        // Label colour matches arc zone
                        // "0" sits before the green arc starts → white
                        Brush lbl_brush;
                        if (rpm == 0)                          lbl_brush = new SolidBrush(FONT_COLOR);
                        else if (rpm <= GREEN_END)             lbl_brush = b_green;
                        else if (rpm <= YELLOW_END)            lbl_brush = b_yellow;
                        else                                   lbl_brush = new SolidBrush(FONT_COLOR); // "8" → white
                        string lbl = (rpm / 1000).ToString();
                        SizeF  sz  = g.MeasureString(lbl, fnt);
                        float  lr  = inner_ring_radius - sz.Width * 0.7f;
                        float  lx  = cx + lr * cos - sz.Width  / 2f;
                        float  ly  = cy + lr * sin - sz.Height / 2f;
                        g.DrawString(lbl, fnt, lbl_brush, lx, ly);
                    }
                }
            }
        }

        // ── Needle (redrawn each paint) ───────────────────────────
        void draw_needle(Graphics g)
        {
            int cx = control_width / 2;
            int cy = control_width / 2;

            float wdeg = rpm_to_windows_deg(current_rpm);
            double rad = deg_2_rad(wdeg);
            float  cos = (float)Math.Cos(rad);
            float  sin = (float)Math.Sin(rad);

            // Needle: thin polygon
            int needle_len  = inner_ring_radius - 4;
            int needle_tail = needle_len / 5;
            int half_w      = Math.Max(2, control_width / 60);

            // Perpendicular direction
            double perp_rad = rad + Math.PI / 2.0;
            float  pcos     = (float)Math.Cos(perp_rad);
            float  psin     = (float)Math.Sin(perp_rad);

            var pts = new PointF[]
            {
                new PointF(cx + needle_len  * cos,          cy + needle_len  * sin),
                new PointF(cx - needle_tail * cos + half_w * pcos,
                           cy - needle_tail * sin + half_w * psin),
                new PointF(cx - needle_tail * cos - half_w * pcos,
                           cy - needle_tail * sin - half_w * psin),
            };

            using (var b = new SolidBrush(Color.White))
                g.FillPolygon(b, pts);
            using (var p = new Pen(Color.Black, 1f))
                g.DrawPolygon(p, pts);

            // Centre hub
            int hub = Math.Max(4, control_width / 30);
            using (var b = new SolidBrush(Color.DarkGray))
                g.FillEllipse(b, cx - hub, cy - hub, hub * 2, hub * 2);
            using (var p = new Pen(Color.White, 1f))
                g.DrawEllipse(p, cx - hub, cy - hub, hub * 2, hub * 2);

            // Digital readout
            using (var fnt = new Font(FONT_FAMILY,
                                       16,
                                       FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.Lime))
            {
                string txt = current_rpm.ToString("D4");
                SizeF  sz  = g.MeasureString(txt, fnt);
                g.DrawString(txt, fnt, b,
                    cx - sz.Width / 2f,
                    cy - (inner_ring_radius * 0.45f) - sz.Height / 2f);
            }
        }

        // ── Conversions ──────────────────────────────────────────
        // Returns Windows angle (0°=right, CW) for a given RPM value.
        static float rpm_to_windows_deg(int rpm)
        {
            // Instrument: GAUGE_START_DEG from top (clockwise) = 0 RPM
            // top-relative → Windows: add 270
            float instrument_deg = GAUGE_START_DEG + (rpm / (float)MAX_RPM) * GAUGE_SWEEP_DEG;
            float windows_deg    = instrument_deg + 270f;
            if (windows_deg >= 360f) windows_deg -= 360f;
            return windows_deg;
        }

        // ── OnPaint ──────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            paint_parent_background(e);

            if (this.Width != control_width || this.Height != control_width)
            {
                control_width = this.Width;
                update_geometry();
                redraw_background = true;
                this.Size = new Size(control_width, control_width);
            }

            if (redraw_background || background_bitmap == null)
                build_background();

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.DrawImageUnscaled(background_bitmap, 0, 0);
            draw_needle(g);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            update_geometry();
            redraw_background = true;
            this.Refresh();
        }

        // ── Helpers ──────────────────────────────────────────────
        static double deg_2_rad(double d) { return Math.PI * d / 180.0; }

        static GraphicsPath rounded_rect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, radius, radius, 180, 90);
            path.AddArc(r.Right - radius, r.Y, radius, radius, 270, 90);
            path.AddArc(r.Right - radius, r.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(r.X, r.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            return path;
        }

        void paint_parent_background(PaintEventArgs e)
        {
            if (this.Parent == null) return;
            var state = e.Graphics.BeginContainer();
            e.Graphics.TranslateTransform(-this.Left, -this.Top);
            var clip = e.ClipRectangle;
            clip.Offset(this.Left, this.Top);
            var pea = new PaintEventArgs(e.Graphics, clip);
            InvokePaintBackground(this.Parent, pea);
            InvokePaint(this.Parent, pea);
            e.Graphics.EndContainer(state);
        }

        void cleanup()
        {
            if (background_bitmap != null) { background_bitmap.Dispose(); background_bitmap = null; }
        }

    } // class RpmIndicator

} // namespace RpmIndicator
