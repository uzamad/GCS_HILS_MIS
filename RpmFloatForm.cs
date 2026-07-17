// ================================================================
//  RpmFloatForm.cs  –  GCS_240626
//
//  Bare container form for the reparented RpmIndicator.
//  When the user clicks X it hides (not disposes) and fires
//  FloatClosed so Form1 can unlatch the RPM button and
//  reparent the indicator back to the main form.
// ================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace GCS_240626
{
    public class RpmFloatForm : Form
    {
        /// <summary>Fired when the user closes the window via the X button.</summary>
        public event Action FloatClosed;

        public RpmFloatForm()
        {
            this.Text            = "RPM Indicator";
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.BackColor       = Color.Black;
            this.ClientSize      = new Size(170, 170);
            this.MinimumSize     = new Size(160, 160);
            this.StartPosition   = FormStartPosition.Manual;
            this.Location        = new Point(150, 150);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;   // don't dispose — just hide
                this.Hide();
                FloatClosed?.Invoke();   // tell Form1 to unlatch the button
            }
            else
            {
                base.OnFormClosing(e);
            }
        }
    }
}
