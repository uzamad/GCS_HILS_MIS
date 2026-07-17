using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HeadingIndicator
{
    // ============================================================
    //  HeadingIndicator – rotating compass rose
    //  Property: CurrentHeading (int, 0-359 degrees)
    //  Self-contained – no Utilities.dll dependency.
    // ============================================================

    public class HeadingIndicator : Control
    {
        // ── Constants ────────────────────────────────────────────
        const int    CONTROL_WIDTH           = 200;
        const int    MINIMUM_CONTROL_WIDTH   = 200;
        const int    MAXIMUM_CONTROL_WIDTH   = 500;
        const int    CONTROL_WIDTH_INCREMENT = 10;
        const string FONT_FAMILY             = "Microsoft Sans Serif";

        static readonly Color BACKGROUND_COLOR    = Color.DimGray;
        static readonly Color INSTRUMENT_BG       = Color.Black;
        static readonly Color FONT_COLOR          = Color.White;

        // ── Fields ───────────────────────────────────────────────
        int   control_width   = CONTROL_WIDTH;
        int   current_heading = 0;
        int   base_font_size  = 8;
        int   instrument_offset;
        int   rose_radius;          // inner usable radius of compass rose
        int   label_radius;         // where text is placed
        int   tick_outer_radius;
        int   tick_inner_major;
        int   tick_inner_minor;

        // Static frame (outer bezel + ring) — only redrawn on resize
        Bitmap frame_bitmap   = null;
        bool   redraw_frame   = true;

        // ── Constructor ──────────────────────────────────────────
        public HeadingIndicator()
        {
            Application.ApplicationExit += (s, e) => cleanup();

            this.BackColor = Color.Black;   // corners blend with form background
            this.Width  = CONTROL_WIDTH;
            this.Height = CONTROL_WIDTH;
            this.SetStyle(ControlStyles.DoubleBuffer |
                          ControlStyles.UserPaint    |
                          ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();
            update_geometry();
        }

        // ── Public property: CurrentHeading ──────────────────────
        [Category("Appearance"),
         Description("Gets/Sets current heading in degrees (0-359)"),
         DefaultValue(0), Bindable(true)]
        public int CurrentHeading
        {
            get { return current_heading; }
            set
            {
                current_heading = ((value % 360) + 360) % 360;
                this.Refresh();
            }
        }

        // ── Public property: ControlWidth ────────────────────────
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
                redraw_frame = true;
                this.Invalidate();
            }
        }

        // ── Geometry ─────────────────────────────────────────────
        void update_geometry()
        {
            instrument_offset  = Math.Max(4, control_width / 20);
            tick_outer_radius  = (control_width / 2) - instrument_offset;
            int ring_width     = Math.Max(4, control_width / 14);
            tick_inner_major   = tick_outer_radius - ring_width;
            tick_inner_minor   = tick_outer_radius - ring_width / 2;
            label_radius       = tick_inner_major - 2;
            rose_radius        = label_radius;

            // Font: larger so labels are clearly readable
            base_font_size = Math.Max(8, ring_width - 1);
        }

        // ── Static frame ─────────────────────────────────────────
        void build_frame()
        {
            if (frame_bitmap != null) frame_bitmap.Dispose();
            frame_bitmap = new Bitmap(control_width, control_width);
            using (Graphics g = Graphics.FromImage(frame_bitmap))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;

                // Outer rounded bezel
                var full = new Rectangle(0, 0, control_width - 1, control_width - 1);
                using (var path = rounded_rect(full, control_width / 10))
                using (var b = new SolidBrush(BACKGROUND_COLOR))
                    g.FillPath(b, path);

                // Black instrument circle
                int off  = instrument_offset / 2;
                int diam = control_width - 1 - 2 * off;
                var circ = new Rectangle(off, off, diam, diam);
                using (var b = new SolidBrush(INSTRUMENT_BG))
                    g.FillEllipse(b, circ);
                using (var p = new Pen(Color.FromArgb(180, FONT_COLOR), 2f))
                    g.DrawEllipse(p, circ);

                // Label: "HDG" inside upper portion of bezel
                int cx = control_width / 2;
                using (var fnt = new Font(FONT_FAMILY,
                                          Math.Max(5, base_font_size - 2),
                                          FontStyle.Bold, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(FONT_COLOR))
                {
                    string lbl = "HDG";
                    SizeF sz = g.MeasureString(lbl, fnt);
                    g.DrawString(lbl, fnt, b,
                        cx - sz.Width / 2f,
                        instrument_offset / 2f);
                }
            }
            redraw_frame = false;
        }

        // ── Rose drawing (rotated each paint) ────────────────────
        void draw_rose(Graphics g)
        {
            int cx = control_width / 2;
            int cy = control_width / 2;

            // Save transform, rotate so that current_heading faces the lubber
            g.TranslateTransform(cx, cy);
            g.RotateTransform(-current_heading);

            using (var pen_major = new Pen(Color.White, 2f))
            using (var pen_minor = new Pen(Color.White, 1f))
            using (var fnt_card  = new Font(FONT_FAMILY,
                                             base_font_size,
                                             FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fnt_num   = new Font(FONT_FAMILY,
                                             Math.Max(5, base_font_size - 2),
                                             FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush_white  = new SolidBrush(Color.White))
            using (var brush_red    = new SolidBrush(Color.Red))
            using (var sf = new StringFormat())
            {
                sf.Alignment     = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                for (int deg = 0; deg < 360; deg += 5)
                {
                    double rad = deg_2_rad(deg - 90.0); // 0° = up
                    float  cos = (float)Math.Cos(rad);
                    float  sin = (float)Math.Sin(rad);

                    bool major    = (deg % 10 == 0);
                    bool cardinal = (deg % 90 == 0);
                    bool thirty   = (deg % 30 == 0);

                    int inner = major ? tick_inner_major : tick_inner_minor;

                    var pen = major ? pen_major : pen_minor;
                    g.DrawLine(pen,
                        inner * cos, inner * sin,
                        tick_outer_radius * cos, tick_outer_radius * sin);

                    if (thirty)
                    {
                        string lbl;
                        Font   fnt;
                        Brush  clr;

                        if      (deg == 0)   { lbl = "N"; fnt = fnt_card; clr = brush_red;   }
                        else if (deg == 90)  { lbl = "E"; fnt = fnt_card; clr = brush_white; }
                        else if (deg == 180) { lbl = "S"; fnt = fnt_card; clr = brush_white; }
                        else if (deg == 270) { lbl = "W"; fnt = fnt_card; clr = brush_white; }
                        else                 { lbl = (deg / 10).ToString("D2");
                                               fnt = fnt_num;  clr = brush_white; }

                        float lr = (float)(label_radius - 2);
                        float lx = lr * cos;
                        float ly = lr * sin;
                        SizeF sz = g.MeasureString(lbl, fnt);
                        var r = new RectangleF(lx - sz.Width / 2f,
                                               ly - sz.Height / 2f,
                                               sz.Width, sz.Height);
                        g.DrawString(lbl, fnt, clr, r, sf);
                    }
                }
            }

            g.ResetTransform();
        }

        // ── Top-down aircraft silhouette (fixed, does not rotate) ────
        void draw_aircraft(Graphics g, int cx, int cy)
        {
            // Scale relative to the inner tick ring so it fills ~40% of the face
            float s  = tick_inner_major * 0.42f;
            float fw = s * 0.09f;   // fuselage half-width

            // ── Fuselage ──────────────────────────────────────────────
            var fuselage = new PointF[]
            {
                new PointF(cx - fw,  cy - s * 0.80f),   // nose-left
                new PointF(cx + fw,  cy - s * 0.80f),   // nose-right
                new PointF(cx + fw,  cy + s * 0.55f),   // tail-right
                new PointF(cx - fw,  cy + s * 0.55f),   // tail-left
            };

            // ── Main wings ────────────────────────────────────────────
            // Swept slightly forward; root at centre-line, tip trailing
            var left_wing = new PointF[]
            {
                new PointF(cx - fw,  cy - s * 0.08f),  // root-front
                new PointF(cx - s,   cy + s * 0.18f),  // tip-front
                new PointF(cx - s,   cy + s * 0.32f),  // tip-back
                new PointF(cx - fw,  cy + s * 0.14f),  // root-back
            };
            var right_wing = new PointF[]
            {
                new PointF(cx + fw,  cy - s * 0.08f),
                new PointF(cx + s,   cy + s * 0.18f),
                new PointF(cx + s,   cy + s * 0.32f),
                new PointF(cx + fw,  cy + s * 0.14f),
            };

            // ── Tail stabilisers ──────────────────────────────────────
            float th = s * 0.38f;   // tail half-span
            var left_tail = new PointF[]
            {
                new PointF(cx - fw,  cy + s * 0.30f),
                new PointF(cx - th,  cy + s * 0.55f),
                new PointF(cx - fw,  cy + s * 0.55f),
            };
            var right_tail = new PointF[]
            {
                new PointF(cx + fw,  cy + s * 0.30f),
                new PointF(cx + th,  cy + s * 0.55f),
                new PointF(cx + fw,  cy + s * 0.55f),
            };

            // Draw filled yellow body with black outline
            using (var fill    = new SolidBrush(Color.Yellow))
            using (var outline = new Pen(Color.Black, 1.2f))
            {
                g.FillPolygon(fill,    fuselage);
                g.FillPolygon(fill,    left_wing);
                g.FillPolygon(fill,    right_wing);
                g.FillPolygon(fill,    left_tail);
                g.FillPolygon(fill,    right_tail);

                g.DrawPolygon(outline, fuselage);
                g.DrawPolygon(outline, left_wing);
                g.DrawPolygon(outline, right_wing);
                g.DrawPolygon(outline, left_tail);
                g.DrawPolygon(outline, right_tail);
            }

            // Small nose cone tip (ellipse)
            using (var b = new SolidBrush(Color.Yellow))
                g.FillEllipse(b, cx - fw, cy - s * 0.88f, fw * 2, fw * 2.2f);

            // Centre hub dot (over the wing join)
            int dotR = Math.Max(2, (int)(fw * 0.8f));
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, cx - dotR, cy - dotR, dotR * 2, dotR * 2);
        }

        // ── Lubber line & centre mark (fixed, screen-space) ──────
        void draw_lubber(Graphics g)
        {
            int cx = control_width / 2;
            int cy = control_width / 2;
            int half = control_width / 2;

            // Triangle lubber line at top
            int tipY  = cy - tick_outer_radius + 2;
            int baseY = tipY + (tick_outer_radius - tick_inner_major) - 2;
            int halfW = Math.Max(3, (tick_outer_radius - tick_inner_major) / 2);

            var tri = new PointF[]
            {
                new PointF(cx,          tipY),
                new PointF(cx - halfW,  baseY),
                new PointF(cx + halfW,  baseY)
            };
            using (var b = new SolidBrush(Color.Orange))
                g.FillPolygon(b, tri);

            // ── Aircraft symbol (fixed, top-down silhouette) ─────────
            draw_aircraft(g, cx, cy);   // draws its own centre hub dot

            // ── Nose-to-outer-ring pointer line ───────────────────────
            // Connects aircraft nose tip straight up to the tick-mark ring,
            // making the heading direction visually unambiguous.
            float s_line   = tick_inner_major * 0.42f;
            float fw_line  = s_line * 0.09f;
            float noseTipY = cy - s_line * 0.88f - fw_line * 1.1f;   // top of nose ellipse
            float ringTopY = cy - tick_outer_radius + 2f;             // lubber triangle tip
            using (var p = new Pen(Color.Yellow, 1.8f))
                g.DrawLine(p, (float)cx, noseTipY, (float)cx, ringTopY);

            // Digital heading readout at bottom
            using (var fnt = new Font(FONT_FAMILY,
                                       Math.Max(9, base_font_size + 2),
                                       FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(Color.Lime))
            {
                string txt = current_heading.ToString("D3") + "°";
                SizeF  sz  = g.MeasureString(txt, fnt);
                g.DrawString(txt, fnt, b,
                    cx - sz.Width  / 2f,
                    cy + tick_inner_major * 0.55f - sz.Height / 2f);
            }
        }

        // ── OnPaint ──────────────────────────────────────────────
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(this.BackColor);  // black corners — no parent bleed-through

            if (this.Width != control_width || this.Height != control_width)
            {
                control_width = this.Width;
                update_geometry();
                redraw_frame = true;
                this.Size = new Size(control_width, control_width);
            }

            if (redraw_frame || frame_bitmap == null)
                build_frame();

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.HighQuality;

            // 1. Static frame
            g.DrawImageUnscaled(frame_bitmap, 0, 0);

            // Clip to instrument circle so rose doesn't bleed into bezel
            int off  = instrument_offset / 2;
            int diam = control_width - 1 - 2 * off;
            using (var clip = new Region(new Rectangle(off, off, diam, diam)))
                g.SetClip(clip, CombineMode.Intersect);

            // 2. Rotating compass rose
            draw_rose(g);

            g.ResetClip();
            g.SmoothingMode = SmoothingMode.HighQuality;

            // 3. Fixed overlay (lubber line, centre dot, readout)
            draw_lubber(g);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            update_geometry();
            redraw_frame = true;
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
            if (frame_bitmap != null) { frame_bitmap.Dispose(); frame_bitmap = null; }
        }

    } // class HeadingIndicator

} // namespace HeadingIndicator
