using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace GCS_240626
{
    /// <summary>
    /// GDI+ artificial horizon (attitude indicator) control.
    ///   Roll  : positive = right wing down (clockwise),  range ±180°
    ///   Pitch : positive = nose up,                      range ±90°
    /// </summary>
    public class ArtificialHorizon : Control
    {
        private double _roll;
        private double _pitch;

        public double Roll
        {
            get { return _roll; }
            set { _roll = value; Invalidate(); }
        }

        public double Pitch
        {
            get { return _pitch; }
            set { _pitch = value; Invalidate(); }
        }

        public ArtificialHorizon()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Size      = new Size(220, 220);
            BackColor = Color.Black;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            int cx = Width  / 2;
            int cy = Height / 2;
            int r  = Math.Min(cx, cy) - 8;

            // ── Clip path (circle) ───────────────────────────────────────────
            GraphicsPath clip = new GraphicsPath();
            clip.AddEllipse(cx - r, cy - r, 2 * r, 2 * r);

            // ── Rotating layer: sky / ground / horizon / pitch ladder ────────
            GraphicsState saved = g.Save();
            g.Clip = new Region(clip);
            g.TranslateTransform(cx, cy);
            g.RotateTransform((float)_roll);
            // ±40° pitch fills the radius; clamp so we do not draw garbage
            double pitchClamped = Math.Max(-40, Math.Min(40, _pitch));
            g.TranslateTransform(0, (float)(pitchClamped * r / 40.0));

            // Sky
            using (SolidBrush sky = new SolidBrush(Color.FromArgb(15, 100, 185)))
                g.FillRectangle(sky, -r * 3, -r * 3, r * 6, r * 3);

            // Ground
            using (SolidBrush gnd = new SolidBrush(Color.FromArgb(110, 65, 20)))
                g.FillRectangle(gnd, -r * 3, 0, r * 6, r * 3);

            // Horizon line
            using (Pen hp = new Pen(Color.White, 2))
                g.DrawLine(hp, -(float)(r * 2.5), 0, (float)(r * 2.5), 0);

            // Pitch ladder  (every 5°, labelled every 10°)
            using (Font lf = new Font("Consolas", 7f))
            {
                for (int p = -30; p <= 30; p += 5)
                {
                    if (p == 0) continue;
                    float py = (float)(-p * r / 40.0);
                    float hw = (p % 10 == 0) ? r * 0.32f : r * 0.16f;
                    using (Pen lp = new Pen(Color.White, 1))
                        g.DrawLine(lp, -hw, py, hw, py);
                    if (p % 10 == 0)
                    {
                        string lbl = Math.Abs(p).ToString();
                        SizeF  sz  = g.MeasureString(lbl, lf);
                        g.DrawString(lbl, lf, Brushes.White,
                                      hw + 3,            py - sz.Height / 2f);
                        g.DrawString(lbl, lf, Brushes.White,
                                     -hw - sz.Width - 3, py - sz.Height / 2f);
                    }
                }
            }

            // ── Fixed layer: aircraft symbol, roll arc, pointer ───────────────
            g.Restore(saved);
            g.ResetClip();

            // Aircraft symbol (fixed, yellow)
            using (Pen ap = new Pen(Color.Yellow, 2))
            {
                int hw = r / 3;
                g.DrawLine(ap, cx - r / 2,  cy,           cx - hw / 2, cy);           // left wing
                g.DrawLine(ap, cx + hw / 2, cy,           cx + r / 2,  cy);           // right wing
                g.DrawLine(ap, cx - hw / 2, cy,           cx,          cy + hw / 3);  // V left
                g.DrawLine(ap, cx + hw / 2, cy,           cx,          cy + hw / 3);  // V right
            }
            g.FillEllipse(Brushes.Yellow, cx - 3, cy - 3, 6, 6);

            // Roll arc (±60° at top)
            int ra = r + 5;
            using (Pen arcPen = new Pen(Color.LightGray, 1))
                g.DrawArc(arcPen,
                          new Rectangle(cx - ra, cy - ra, 2 * ra, 2 * ra),
                          210f, 120f);   // 210° to 330° = top portion

            // Tick marks on arc
            int[]   ticks = { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 };
            float[] tlens = {    8,   6,   8,   5,   5, 10,  5,  5,  8,  6,  8 };
            for (int i = 0; i < ticks.Length; i++)
            {
                // Screen angle: 270° is the top; positive roll rotates CW
                double ang = (270 - ticks[i]) * Math.PI / 180.0;
                float  tl  = tlens[i];
                float  x1  = cx + (float)(ra             * Math.Cos(ang));
                float  y1  = cy + (float)(ra             * Math.Sin(ang));
                float  x2  = cx + (float)((ra - tl)     * Math.Cos(ang));
                float  y2  = cy + (float)((ra - tl)     * Math.Sin(ang));
                using (Pen tp = new Pen(Color.LightGray, 1))
                    g.DrawLine(tp, x1, y1, x2, y2);
            }

            // Roll indicator pointer (yellow triangle, moves with roll)
            double rollAng = (270.0 - _roll) * Math.PI / 180.0;
            PointF tip = new PointF(
                cx + (float)((ra - 14) * Math.Cos(rollAng)),
                cy + (float)((ra - 14) * Math.Sin(rollAng)));
            double la  = rollAng + 0.12;
            double ra2 = rollAng - 0.12;
            PointF lpt = new PointF(
                cx + (float)((ra + 3) * Math.Cos(la)),
                cy + (float)((ra + 3) * Math.Sin(la)));
            PointF rpt = new PointF(
                cx + (float)((ra + 3) * Math.Cos(ra2)),
                cy + (float)((ra + 3) * Math.Sin(ra2)));
            g.FillPolygon(Brushes.Yellow, new PointF[] { tip, lpt, rpt });

            // Border ring
            using (Pen bp = new Pen(Color.FromArgb(70, 70, 70), 4))
                g.DrawEllipse(bp, cx - r - 1, cy - r - 1, (r + 1) * 2, (r + 1) * 2);

            clip.Dispose();
        }
    }
}
