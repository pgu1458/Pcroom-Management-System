using System;
using System.Runtime.InteropServices; // 외부 DLL을 쓰기 위해 필요합니다.
using System.Drawing;
using System.Windows.Forms;
using System.Data.SQLite;
using System.IO;

namespace test
{
    public partial class CounterLogin : UserControl
    {
        // ── 회원가입 패널 관련 컨트롤 ──────────────────
        private Panel pnlRegister;
        private TextBox txtRegId, txtRegName, txtRegBirth, txtRegPw, txtRegPhone;


        [DllImport("user32.dll", CharSet = CharSet.Auto)]//텍스트박스 placeHolder
        private static extern Int32 SendMessage(IntPtr hWnd, int msg, int wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        private const int EM_SETCUEBANNER = 0x1501; // Placeholder 신호 번호

        public CounterLogin()
        {
            InitializeComponent();

            btnLogin.Click += btnLogin_Click;
            btnLogin.Enter += btnLogin_Click;

            tbPw.KeyDown += tbPw_KeyDown;

            //place Holer용
            SendMessage(tbId.Handle, EM_SETCUEBANNER, 0, "아이디");
            SendMessage(tbPw.Handle, EM_SETCUEBANNER, 0, "비밀번호");

        }



        private void btnLogin_Click(object sender, EventArgs e)
        {
            string id = tbId.Text.Trim();
            string pw = tbPw.Text.Trim();

            if (id.Length == 0 || pw.Length == 0)
            {
                MessageBox.Show("아이디와 비밀번호를 입력해주세요.");
                return;
            }

            string role = GetRole(id, pw);

            if (role == null)
            {
                MessageBox.Show("아이디 또는 비밀번호가 틀렸습니다.");
                return;
            }

            Form1 parentForm = (Form1)this.FindForm();

            if (role == "관리자")
            {
                parentForm.ShowAdmin();
            }
            else if (role == "알바")
            {
                parentForm.ShowAlba();
            }
            else
            {
                MessageBox.Show("손님은 키오스크를 이용해주세요.");
            }
        }

        // DB에서 아이디/비번 확인 후 role 반환
        private string GetRole(string id, string pw)
        {
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Member.db");
            string connString = "Data Source=" + dbPath + ";Version=3;";

            using (SQLiteConnection conn = new SQLiteConnection(connString))
            {
                conn.Open();
                string query = "SELECT role FROM Member WHERE id=@ID AND password=@PW";
                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@ID", id);
                    cmd.Parameters.AddWithValue("@PW", pw);
                    object result = cmd.ExecuteScalar();
                    return result != null ? result.ToString() : null;
                }
            }
        }

        private void tbPw_KeyDown(object sender, KeyEventArgs e)//로그인 엔터키
        {
            if (e.KeyCode == Keys.Enter)
            {
                // 엔터키를 눌렀을 때 로그인 버튼 클릭 이벤트를 강제로 발생시킴
                btnLogin_Click(btnLogin, EventArgs.Empty);
                // 엔터 입력 시 '띵' 하는 경고음 방지
                e.SuppressKeyPress = true;
            }
        }
    }
}