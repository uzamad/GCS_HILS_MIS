// PacketMonitorForm.cs
// Diagnostic window — shows all 8 PacketData[] arrays as live hex dumps.
// Open from Form1 via the "PKT MON" button.
// Refreshes every 200 ms on the selected tab, flicker-free via WM_SETREDRAW.
//
// Colour coding:
//   Green  = sync bytes  [0..2]  AA 55 DD
//   Yellow = packet number [3]   01..08
//   White  = payload     [4..159]
//   Cyan   = CRC         [160..163]

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GCS_240626
{
    public class PacketMonitorForm : Form
    {
        // ── Win32 — suppress redraws while rebuilding RTB content ─────────
        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, bool wParam, int lParam);
        private const int WM_SETREDRAW = 11;

        // ── Layout ────────────────────────────────────────────────────────
        private readonly Label       _lblStatus;
        private readonly TabControl  _tabs = new TabControl();
        private readonly RichTextBox[] _rtb = new RichTextBox[8];
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();

        // ── Colours ───────────────────────────────────────────────────────
        private static readonly Color BG       = Color.FromArgb(12, 12, 12);
        private static readonly Color CLR_SYNC = Color.FromArgb(0, 230, 0);
        private static readonly Color CLR_PKTN = Color.Yellow;
        private static readonly Color CLR_DATA = Color.FromArgb(200, 200, 200);
        private static readonly Color CLR_CRC  = Color.Cyan;
        private static readonly Color CLR_HEAD = Color.FromArgb(140, 140, 140);
        private static readonly Color CLR_SEP  = Color.FromArgb(55, 55, 55);

        // ── Packet layout (must match PacketReceiver) ─────────────────────
        private const int PACKET_LEN = 164;
        private const int CRC_OFFSET = 160;

        // ─────────────────────────────────────────────────────────────────
        public PacketMonitorForm()
        {
            Text        = "Packet Monitor — raw hex view";
            ClientSize  = new Size(960, 720);
            BackColor   = BG;
            ForeColor   = Color.Lime;
            Font        = new Font("Consolas", 10f, FontStyle.Regular);
            MinimumSize = new Size(700, 400);

            // ── Status bar ───────────────────────────────────────────────
            _lblStatus = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 28,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Lime,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding   = new Padding(8, 0, 0, 0),
                Font      = new Font("Consolas", 10f, FontStyle.Bold)
            };

            // ── Tabs ─────────────────────────────────────────────────────
            _tabs.Dock      = DockStyle.Fill;
            _tabs.BackColor = BG;
            _tabs.ForeColor = Color.Lime;

            for (int i = 0; i < 8; i++)
            {
                var page = new TabPage($"  PKT {i + 1}  ")
                {
                    BackColor = BG,
                    ForeColor = Color.Lime,
                    Padding   = new Padding(4)
                };

                var rtb = new RichTextBox
                {
                    Dock        = DockStyle.Fill,
                    BackColor   = BG,
                    ForeColor   = CLR_DATA,
                    Font        = new Font("Consolas", 10f, FontStyle.Regular),
                    ReadOnly    = true,
                    ScrollBars  = RichTextBoxScrollBars.Vertical,
                    BorderStyle = BorderStyle.None,
                    WordWrap    = false
                };
                _rtb[i] = rtb;
                page.Controls.Add(rtb);
                _tabs.TabPages.Add(page);
            }

            // Add controls (status bar last → Dock=Top takes priority)
            Controls.Add(_tabs);
            Controls.Add(_lblStatus);

            // ── Timer ────────────────────────────────────────────────────
            _timer.Interval = 200;
            _timer.Tick    += OnTick;
            _timer.Start();

            OnTick(null, EventArgs.Empty);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosed(e);
        }

        // ── Timer ─────────────────────────────────────────────────────────
        private void OnTick(object sender, EventArgs e)
        {
            // Skip the expensive RTB render when the window is hidden or minimised.
            // The timer keeps running so the status is live the moment it's shown.
            if (!Visible || WindowState == FormWindowState.Minimized) return;

            _lblStatus.Text =
                $"  RxBytes: {PacketReceiver.RawBytesCount,8:N0}   " +
                $"CRC Fails: {PacketReceiver.CrcFailCounter,5}   " +
                $"Packets OK: {CountOK()}/8";

            int tab = _tabs.SelectedIndex;
            if (tab >= 0) RenderPacket(tab);
        }

        // ── Render one packet — flicker-free ──────────────────────────────
        private void RenderPacket(int idx)
        {
            var    rtb      = _rtb[idx];
            byte[] data     = PacketReceiver.PacketData[idx];
            bool   received = PacketReceiver.PacketReceived[idx];

            // Save scroll position so the view doesn't jump on each refresh
            int firstChar  = rtb.GetCharIndexFromPosition(new System.Drawing.Point(0, 0));
            int firstLine  = rtb.GetLineFromCharIndex(firstChar);

            // Freeze redraws
            SendMessage(rtb.Handle, WM_SETREDRAW, false, 0);

            rtb.Clear();

            // ── Header ───────────────────────────────────────────────────
            Append(rtb, $"Packet {idx + 1}   ", CLR_HEAD);
            if (received)
                Append(rtb, "CRC OK  ✓\n", Color.Lime);
            else
                Append(rtb, "Not yet received\n", Color.Gray);

            Append(rtb, "\nOffset   00 01 02 03 04 05 06 07   08 09 0A 0B 0C 0D 0E 0F    ASCII\n", CLR_HEAD);
            Append(rtb, new string('─', 75) + "\n", CLR_SEP);

            // ── Hex rows ─────────────────────────────────────────────────
            for (int row = 0; row < PACKET_LEN; row += 16)
            {
                Append(rtb, $"[{row:X3}]    ", CLR_HEAD);

                for (int col = 0; col < 16; col++)
                {
                    int bi = row + col;
                    if (bi >= PACKET_LEN)
                    {
                        Append(rtb, "   ", BG);
                        if (col == 7) Append(rtb, " ", BG);
                        continue;
                    }
                    Append(rtb, $"{data[bi]:X2}", ByteColor(bi));
                    Append(rtb, col == 7 ? "   " : " ", BG);
                }

                // ASCII panel
                Append(rtb, "   ", BG);
                for (int col = 0; col < 16; col++)
                {
                    int bi = row + col;
                    if (bi >= PACKET_LEN) break;
                    char c = (data[bi] >= 0x20 && data[bi] < 0x7F) ? (char)data[bi] : '.';
                    Append(rtb, c.ToString(), ByteColor(bi));
                }
                Append(rtb, "\n", BG);
            }

            // ── CRC summary ──────────────────────────────────────────────
            uint crcStored = (uint)(data[160]
                           | (data[161] << 8)
                           | (data[162] << 16)
                           | (data[163] << 24));
            Append(rtb, $"\nCRC stored in packet:  0x{crcStored:X8}\n", CLR_CRC);

            // Colour key
            Append(rtb, "\n[", CLR_HEAD);
            Append(rtb, "AA 55 DD", CLR_SYNC);
            Append(rtb, " = sync]  [", CLR_HEAD);
            Append(rtb, "PKT#", CLR_PKTN);
            Append(rtb, " = pkt num]  [", CLR_HEAD);
            Append(rtb, "CRC", CLR_CRC);
            Append(rtb, " = CRC bytes]\n", CLR_HEAD);

            // Restore scroll position
            if (firstLine > 0)
            {
                int targetChar = rtb.GetFirstCharIndexFromLine(firstLine);
                if (targetChar >= 0)
                {
                    rtb.SelectionStart = targetChar;
                    rtb.ScrollToCaret();
                }
            }

            // Unfreeze — single repaint
            SendMessage(rtb.Handle, WM_SETREDRAW, true, 0);
            rtb.Invalidate();
        }

        // ── Helpers ───────────────────────────────────────────────────────
        private static Color ByteColor(int idx)
        {
            if (idx <= 2)          return CLR_SYNC;
            if (idx == 3)          return CLR_PKTN;
            if (idx >= CRC_OFFSET) return CLR_CRC;
            return CLR_DATA;
        }

        private static void Append(RichTextBox rtb, string text, Color color)
        {
            rtb.SelectionStart  = rtb.TextLength;
            rtb.SelectionLength = 0;
            rtb.SelectionColor  = color;
            rtb.AppendText(text);
        }

        private static int CountOK()
        {
            int n = 0;
            for (int i = 0; i < 8; i++)
                if (PacketReceiver.PacketReceived[i]) n++;
            return n;
        }
    }
}
