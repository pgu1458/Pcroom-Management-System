using System.Drawing.Drawing2D;

namespace Kiosk_Sum
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button52_Paint(object sender, PaintEventArgs e)
        {
            Button btn = (Button)sender;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(btn.Parent.BackColor);
            int radius = 50;
            int borderWidth = 2;
            int shadowOffset = 4;
            Color borderColor = Color.DarkGray;
            Rectangle rect = new Rectangle(0, 0, btn.Width - shadowOffset, btn.Height - shadowOffset);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
            path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
            path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            using (Brush shadowBrush = new SolidBrush(Color.FromArgb(60, Color.Black)))
            {
                GraphicsPath shadowPath = (GraphicsPath)path.Clone();
                Matrix matrix = new Matrix();
                matrix.Translate(shadowOffset, shadowOffset);
                shadowPath.Transform(matrix);
                g.FillPath(shadowBrush, shadowPath);
            }
            using (Brush btnBrush = new SolidBrush(btn.BackColor))
            {
                g.FillPath(btnBrush, path);
            }
            using (Pen pen = new Pen(borderColor, borderWidth))
            {
                pen.Alignment = PenAlignment.Inset;
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, btn.Text, btn.Font, rect, btn.ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // ═══════════════════════════════════════════════
        //  ★ 충전 button1 클릭 → KioskLogin으로 전환
        // ═══════════════════════════════════════════════
        private void button1_Click(object sender, EventArgs e)
        {
            this.Controls.Clear();

            KioskLogin login = new KioskLogin();
            login.Size = ClientSize;
            login.Location = new Point(0, 0);
            login.BringToFront();
            this.Controls.Add(login);
        }

        // ═══════════════════════════════════════════════
        //  ★ 첫화면 복귀 (충전 완료 후 호출)
        //    Controls를 다시 초기화하여 첫화면으로 돌아감
        // ═══════════════════════════════════════════════
        public void ShowFirstScreen()
        {
            this.Controls.Clear();
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            // 현재 폼이 있는 스크린의 전체 크기를 가져와서 설정
            this.Bounds = Screen.PrimaryScreen.Bounds;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Esc 키를 누르면 프로그램 즉시 종료
            if (keyData == Keys.Escape)
            {
                Application.Exit(); // 또는 this.Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void button52_Click(object sender, EventArgs e)
        {
            this.Controls.Clear();
            this.AutoScroll = true;

            KioskSeat seat = new KioskSeat();
            seat.Dock = DockStyle.None;
            seat.Size = new Size(2500, 1080);
            seat.Location = new Point(0, 0);
            this.Controls.Add(seat);
        }
    }
}