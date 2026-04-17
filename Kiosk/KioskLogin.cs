using System;
using System.Windows.Forms;


namespace Kiosk_Sum
{
    public partial class KioskLogin : UserControl
    {
        public KioskLogin()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            InitializeComponent();
            // 로그인 버튼 이벤트 연결
            btn_Log_In.Click += Btn_Log_In_Click;

            // 비밀번호 엔터 키 → 로그인 실행
            textBox2.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    Btn_Log_In_Click(s, e);
                }
            };
        }

        // ═══════════════════════════════════════════════
        //  로그인 버튼 클릭
        //  → 9002 포트: LOGIN|id|pw
        //  ← 9002 포트: LOGIN_OK|name|remain / LOGIN_FAIL
        // ═══════════════════════════════════════════════
        private async void Btn_Log_In_Click(object sender, EventArgs e)
        {
            string id = textBox1.Text.Trim();   // 앞뒤 공백제거 후 아이디
            string pw = textBox2.Text.Trim();   // 앞뒤 공백제거 후 비밀번호

            if (id.Length == 0 || pw.Length == 0)   //아이디 비번이 입력 안됐을경우에
            {
                MessageBox.Show("아이디와 비밀번호를 입력해주세요.", "로그인");
                return;
            }

            btn_Log_In.Enabled = false;     //로그인버튼 비활성화
            btn_Log_In.Text = "연결 중...";

            try
            {
                // 서버 연결 (최초 1회)
                bool connected = await KioskTcpClient.Instance.ConnectAsync();      //관리자 pc와 연결이 되어 있지 않은경우 
                if (!connected)     //관리자pc연결 되어 있지 않으면 메세지박스를 띄움
                {
                    MessageBox.Show("서버에 연결할 수 없습니다.\n관리자 PC가 켜져 있는지 확인해주세요.", "연결 실패");
                    return;
                }

                // 로그인 요청
                KioskLoginResult result = await KioskTcpClient.Instance.LoginAsync(id, pw); //입력받은 id,pw를 관리자pc에 보냄

                if (result.Success)
                {
                    //Member_Charge 화면으로 전환
                    //부모 컨테이너(tabPage1 등)에서 KioskLogin을 제거하고 Member_Charge를 추가
                   Control parent = this.Parent;
                    if (parent != null)
                    {
                        parent.Controls.Clear();    //기존로그인화면 클리어
                        Member_Charge charge = new Member_Charge(); //Member_Charge화면 띄움
                        charge.Dock = DockStyle.Fill;       //Dock을 Fill로
                        parent.Controls.Add(charge);        
                    }
                    //Form1 pan = new Form1();
                    //pan.panel1.Controls.Clear();
                    //Member_Charge charge = new Member_Charge();
                    //pan.panel1.Controls.Add(charge);
                }
                else
                {
                    MessageBox.Show(result.Message, "로그인 실패");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("오류: " + ex.Message, "오류");     //예상치못한 오류가 뜰 경우
            }
            finally     //로그인 실패 성공 여부를 떠나 실행됨
            {
                btn_Log_In.Enabled = true;      //로그인버튼 활성화
                btn_Log_In.Text = "로그인";      //로그인버튼안에 텍스트에 '로그인'작성
            }
        }
    }
}