using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VerticalSpeedIndicator
{
    // ============================================================
    //  VerticalSpeedIndicator – ±2000 ft/min sweep gauge
    //  0 ft/min is at the top. Climb (positive) sweeps right,
    //  descent (negative) sweeps left.  Scale: 135° each side.
    //  Property: CurrentVerticalSpeed (float, -2000 to +2000)
    //  Self-contained – no Utilities.dll dependency.
    // ============================================================

    public class VerticalSpeedIndicator : Control
    {
        // ── Constants ────────────────────────────────────────────
        const int    CONTROL_WIDTH           = 200;
        const int    MINIMUM_CONTROL_WIDTH   = 150;
        const int    MAXIMUM_CONTROL_WIDTH   = 500;
        const int    CONTROL_WIDTH_INCREMENT = 50;
        const string FONT_FAMILY             = "Microsoft Sans Serif";

        const float MAX_VS        = 2000f;  // ft/min
        const float HALF_SWEEP    = 135f;   // degrees each side from top

        static readonly Color BACKGROUND_COLOR = Color.DimGray;
        static readonly Color INSTRUMENT_BG    = Color.Gray;
        static readonly Color FONT_COLOR       = Color.White;

        // ── Fields ───────────────────────────────────────────────
        int   control_width     = CONTROL_WIDTH;
        float current_vs        = 0f;
        int   base_font_size    = 8;
        int   instrument_offset;
        int   outer_ring_radius;
        int   inner_ring_radius;
        int   mid_ring_radius;

        Bitmap background_bitmap = null;
        bool   redraw_background = true;

        // ── Constructor ──────────────────────────────────────────
        public VerticalSpeedIndicator()
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
         Description("Gets/Sets current vertical speed in ft/min (-2000 to +2000)"),
         DefaultValue(0f), Bindable(true)]
        public float CurrentVerticalSpeed
        {
            get { return current_vs; }
            set
            {
                current_vs = Math.Max(-MAX_VS, Math.Min(MAX_VS, value));
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
            int ring_width     = Math.Max(4, control_width / 14);
            inner_ring_radius  = outer_ring_radius - ring_width;
            mid_ring_radius    = (outer_ring_radius + inner_ring_radius) / 2;
            base_font_size     = Math.Max(6, ring_width - 4);
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

                // Colour bands: climb=green arc on right side, descent=yellow on left
                draw_arc_band(g, cx, cy, outer_ring_radius, inner_ring_radius,
                    0f, MAX_VS, Color.FromArgb(180, Color.Lime));
                draw_arc_band(g, cx, cy, outer_ring_radius, inner_ring_radius,
                    -MAX_VS, 0f, Color.FromArgb(180, Color.SteelBlue));

                // Tick marks
                draw_ticks_and_labels(g, cx, cy);

                // UP / DOWN arrows and title
                draw_labels(g, cx, cy);
            }
            redraw_background = false;
        }

        void draw_arc_band(Graphics g, int cx, int cy,
                           int outerR, int innerR,
                           float fromVs, float toVs, Color color)
        {
            float startDeg = vs_to_windows_deg(fromVs);
            float endDeg   = vs_to_windows_deg(toVs);
            float sweepDeg = endDeg - startDeg;

            int d_outer = outerR * 2;
            int d_inner = innerR * 2;
            var outer_rect = new Rectangle(cx - outerR, cy - outerR, d_outer, d_outer);
            var inner_rect = new Rectangle(cx - innerR, cy - innerR, d_inner, d_inner);

            var outer_path = new GraphicsPath(); outer_path.AddEllipse(outer_rect);
            var inner_path = new GraphicsPath(); inner_path.AddEllipse(inner_rect);
            var outer_reg  = new Region(outer_path);
            var inner_reg  = new Region(inner_path);
            outer_reg.Exclude(inner_reg);

            g.SetClip(outer_reg, CombineMode.Intersect);
            using (var b = new SolidBrush(color))
                g.FillPie(b, outer_rect, startDeg, sweepDeg);
            g.ResetClip();

            outer_reg.Dispose(); inner_reg.Dispose();
            outer_path.Dispose(); inner_path.Dispose();
        }

        void draw_ticks_and_labels(Graphics g, int cx, int cy)
        {
            // Values: 0, ±500, ±1000, ±1500, ±2000
            // Minor ticks every 250 ft/min
            using (var pen_major = new Pen(Color.White, 2f))
            using (var pen_minor = new Pen(Color.White, 1f))
            using (var fnt = new Font(FONT_FAMILY,
                                       Math.Max(5, base_font_size - 1),
                                       FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(FONT_COLOR))
            {
                for (int vs_i = -(int)MAX_VS; vs_i <= (int)MAX_VS; vs_i += 250)
                {
                    float vs    = vs_i;
                    bool major  = (vs_i % 500 == 0);
                    float wdeg  = vs_to_windows_deg(vs);
                    double rad  = deg_2_rad(wdeg);
                    float  cos  = (float)Math.Cos(rad);
                    float  sin  = (float)Math.Sin(rad);

                    int inner = major ? inner_ring_radius : mid_ring_radius;
                    var pen   = major ? pen_major : pen_minor;

                    g.DrawLine(pen,
                        cx + inner * cos,              cy + inner * sin,
                        cx + outer_ring_radius * cos,  cy + outer_ring_radius * sin);

                    if (major && vs_i != 0)
                    {
                        string lbl  = Math.Abs(vs_i / 100).ToString(); // "5", "10", "15", "20"
                        SizeF  sz   = g.MeasureString(lbl, fnt);
                        float  lr   = inner_ring_radius - sz.Width * 0.8f;
                        float  lx   = cx + lr * cos - sz.Width  / 2f;
                        float  ly   = cy + lr * sin - sz.Height / 2f;
                        g.DrawString(lbl, fnt, b, lx, ly);
                    }
                }

                // Draw "0" at the top
                float zero_wdeg = vs_to_windows_deg(0f);
                double zero_rad = deg_2_rad(zero_wdeg);
                float  zcos     = (float)Math.Cos(zero_rad);
                float  zsin     = (float)Math.Sin(zero_rad);
                string zlbl     = "0";
                SizeF  zsz      = g.MeasureString(zlbl, fnt);
                float  zlr      = inner_ring_radius - zsz.Width * 0.8f;
                g.DrawString(zlbl, fnt, b,
                    cx + zlr * zcos - zsz.Width  / 2f,
                    cy + zlr * zsin - zsz.Height / 2f);
            }
        }

        void draw_labels(Graphics g, int cx, int cy)
        {
            // Title at centre-top
            using (var fnt = new Font(FONT_FAMILY,
                                       Math.Max(6, base_font_size),
                                       FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(FONT_COLOR))
            {
                string title = "VERT SPEED";
                SizeF  sz    = g.MeasureString(title, fnt);
                g.DrawString(title, fnt, b, cx - sz.Width / 2f, cy * 0.35f);

                using (var fnt2 = new Font(FONT_FAMILY,
                                            Math.Max(5, base_font_size - 2),
                                            FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    string sub = "x100 FT/MIN";
                    SizeF  sz2 = g.MeasureString(sub, fnt2);
                    g.DrawString(sub, fnt2, b,
                        cx - sz2.Width / 2f, cy * 0.35f + sz.Height + 1);
                }
            }

            // UP arrow label on right side
            using (var fnt = new Font(FONT_FAMILY,
                                       Math.Max(5, base_font_size - 2),
                                       FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.Lime))
            {
                string lbl = "UP ▲";
                SizeF  sz  = g.MeasureString(lbl, fnt);
                // Place at ~45° right of centre
                float angle = vs_to_windows_deg(MAX_VS * 0.6f);
                double rad  = deg_2_rad(angle);
                float  lr   = (float)(inner_ring_radius * 0.65);
                g.DrawString(lbl, fnt, b,
                    cx + (float)(lr * Math.Cos(rad)) - sz.Width / 2f,
                    cy + (float)(lr * Math.Sin(rad)) - sz.Height / 2f);
            }

            // DOWN arrow label on left side
            using (var fnt = new Font(FONT_FAMILY,
                                       Math.Max(5, base_font_size - 2),
                                       FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.SteelBlue))
            {
                string lbl = "▼ DN";
                SizeF  sz  = g.MeasureString(lbl, fnt);
                float angle = vs_to_windows_deg(-MAX_VS * 0.6f);
                double rad  = deg_2_rad(angle);
                float  lr   = (float)(inner_ring_radius * 0.65);
                g.DrawString(lbl, fnt, b,
                    cx + (float)(lr * Math.Cos(rad)) - sz.Width / 2f,
                    cy + (float)(lr * Math.Sin(rad)) - sz.Height / 2f);
            }
        }

        // ── Needle ───────────────────────────────────────────────
        void draw_needle(Graphics g)
        {
            int cx = control_width / 2;
            int cy = control_width / 2;

            float wdeg = vs_to_windows_deg(current_vs);
            double rad = deg_2_rad(wdeg);
            float  cos = (float)Math.Cos(rad);
            float  sin = (float)Math.Sin(rad);

            int needle_len  = inner_ring_radius - 4;
            int needle_tail = needle_len / 5;
            int half_w      = Math.Max(2, control_width / 60);

            double perp_rad = rad + Math.PI / 2.0;
            float  pcos     = (float)Math.Cos(perp_rad);
            float  psin     = (float)Math.Sin(perp_rad);

            // Colour needle based on direction
            Color needle_color = (current_vs > 0)  ? Color.Lime :
                                 (current_vs < 0)  ? Color.DodgerBlue :
                                                     Color.White;

            var pts = new PointF[]
            {
                new PointF(cx + needle_len  * cos,          cy + needle_len  * sin),
                new PointF(cx - needle_tail * cos + half_w * pcos,
                           cy - needle_tail * sin + half_w * psin),
                new PointF(cx - needle_tail * cos - half_w * pcos,
                           cy - needle_tail * sin - half_w * psin),
            };

            using (var b = new SolidBrush(needle_color))
                g.FillPolygon(b, pts);
            using (var p = new Pen(Color.Black, 1f))
                g.DrawPolygon(p, pts);

            // Hub
            int hub = Math.Max(4, control_width / 30);
            using (var b = new SolidBrush(Color.DarkGray))
                g.FillEllipse(b, cx - hub, cy - hub, hub * 2, hub * 2);
            using (var p = new Pen(Color.White, 1f))
                g.DrawEllipse(p, cx - hub, cy - hub, hub * 2, hub * 2);

            // Digital readout at bottom
            using (var fnt = new Font(FONT_FAMILY,
                                       Math.Max(6, base_font_size),
                                       FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(needle_color))
            {
                string txt = (current_vs >= 0 ? "+" : "") + ((int)current_vs).ToString();
                SizeF  sz  = g.MeasureString(txt, fnt);
                g.DrawString(txt, fnt, b,
                    cx - sz.Width / 2f,
                    cy + inner_ring_radius * 0.55f - sz.Height / 2f);
            }
        }

        // ── Conversion ───────────────────────────────────────────
        // Maps VS (-2000..+2000) to Windows angle (0°=right, CW)
        // VS=0 → instrument 0° (top) → Windows 270°
        // VS=+MAX → instrument +HALF_SWEEP → Windows 270+135=405→45°
        // VS=-MAX → instrument -HALF_SWEEP → Windows 270-135=135°
        static float vs_to_windows_deg(float vs)
        {
            float instrument_deg = (vs / MAX_VS) * HALF_SWEEP; // ±135°
            float windows_deg    = instrument_deg + 270f;
            if (windows_deg < 0f)   windows_deg += 360f;
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

    } // class VerticalSpeedIndicator

} // namespace VerticalSpeedIndicator
