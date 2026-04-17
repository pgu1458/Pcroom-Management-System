using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Kiosk_Sum
{
    public partial class Member_Charge : UserControl
    {
        // 선택된 충전 시간 (초 단위)
        private int _chargeSeconds = 0;
        int num = 0;

        public Member_Charge()
        {
            InitializeComponent();

            this.Load += Member_Charge_Load;

            // ── 충전 시간 선택 버튼 이벤트 연결 (button_1 ~ button_5) ──
            button_1.Click += ChargeButton_Click;
            button_2.Click += ChargeButton_Click;
            button_3.Click += ChargeButton_Click;
            button_4.Click += ChargeButton_Click;
            button_5.Click += ChargeButton_Click;
            bigLabel1.Click += ChargeButton_Click;
            bigLabel2.Click += ChargeButton_Click;
            bigLabel3.Click += ChargeButton_Click;
            bigLabel4.Click += ChargeButton_Click;
            bigLabel5.Click += ChargeButton_Click;

            // ── 결제 버튼 이벤트 연결 ──
            CreditCard.Click += PayButton_Click;
            KaKaoPay.Click += PayButton_Click;
            NaverPay.Click += PayButton_Click;
            label3.Click += PayButton_Click;
            label5.Click += PayButton_Click;
            label6.Click += PayButton_Click;
        }

        // ═══════════════════════════════════════════════
        //  화면 로드 시 로그인 회원 정보 표시
        //  KioskTcpClient.Instance에 저장된 로그인 상태 사용
        // ═══════════════════════════════════════════════
        private void Member_Charge_Load(object sender, EventArgs e)
        {
            var client = KioskTcpClient.Instance;

            if (client.IsLoggedIn)
            {
                // 로그인 된 상태 → 회원 아이디, 이름, 남은시간 표시
                UserID.Text = client.CurrentUserId ?? "-";
                UserName.Text = client.CurrentUserName ?? "-";
                RemainTime.Text = FormatTimeDisplay(client.CurrentRemainTime);
            }
            else
            {
                UserID.Text = "-";
                UserName.Text = "-";
                RemainTime.Text = "-";
            }

            // 충전시간 초기화
            ChargeTime.Text = "0시간";
            _chargeSeconds = 0;
        }

        // ═══════════════════════════════════════════════
        //  충전 시간 선택 버튼 클릭 (button_1 ~ button_5)
        //  각 버튼에 대응하는 bigLabel의 시간 텍스트를 파싱하여
        //  ChargeTime 라벨에 표시하고 _chargeSeconds에 저장
        //
        //  button_1 → bigLabel1 (50,000원 / 60시간)
        //  buttone_2 → bigLabel2 (30,000원 / 35시간)
        //  button_3 → bigLabel3 (10,000원 / 10시간)
        //  button_4 → bigLabel4 (3,000원 / 3시간30분)
        //  button_5 → bigLabel5 (1,000원 / 1시간)
        // ═══════════════════════════════════════════════
        private void ChargeButton_Click(object sender, EventArgs e)
        {
            // 로그인 확인
            if (!KioskTcpClient.Instance.IsLoggedIn)
            {
                MessageBox.Show("먼저 로그인 해주세요.", "로그인 필요");
                return;
            }

            string timeText = "";

            if (sender == button_1 || sender == bigLabel1) 
                timeText = GetTimeLineFromLabel(bigLabel1.Text);   // "60시간"
            else if (sender == button_2 || sender == bigLabel2)
                timeText = GetTimeLineFromLabel(bigLabel2.Text);   // "35시간"
            else if (sender == button_3 || sender == bigLabel3)
                timeText = GetTimeLineFromLabel(bigLabel3.Text);   // "10시간"
            else if (sender == button_4 || sender == bigLabel4)
                timeText = GetTimeLineFromLabel(bigLabel4.Text);   // "3시간30분"
            else if (sender == button_5 || sender == bigLabel5)
                timeText = GetTimeLineFromLabel(bigLabel5.Text);   // "1시간"

            // 시간 텍스트를 초(seconds)로 변환
            _chargeSeconds = ParseTimeToSeconds(timeText);

            // ChargeTime 라벨에 표시
            ChargeTime.Text = timeText;
        }

        // ═══════════════════════════════════════════════
        //  결제 버튼 클릭 (CreditCard / KaKaoPay / NaverPay)
        //  서버로 CHARGE|id|addSeconds 전송 → 충전 완료
        // ═══════════════════════════════════════════════
        private async void PayButton_Click(object sender, EventArgs e)
        {
            // 로그인 확인
            if (!KioskTcpClient.Instance.IsLoggedIn)
            {
                MessageBox.Show("먼저 로그인 해주세요.", "로그인 필요");
                return;
            }

            if (_chargeSeconds <= 0)
            {
                MessageBox.Show("충전할 시간을 먼저 선택해주세요.", "충전");
                return;
            }

            var client = KioskTcpClient.Instance;

            // 결제 수단 이름
            string payMethod = "";
            if (sender == CreditCard || sender == label3)
            {
                payMethod = "신용카드";
                num = 1;
                
            }
            else if (sender == KaKaoPay || sender == label5)
            {
                payMethod = "카카오페이";
                num = 2;
            }
            else if (sender == NaverPay || sender == label8)
            {
                payMethod = "네이버페이";
                num = 3;
            }

            // 확인 메시지
            DialogResult confirm = MessageBox.Show(
                "충전 시간: " + ChargeTime.Text + "\n" +
                "결제 수단: " + payMethod + "\n\n" +
                "충전하시겠습니까?",
                "충전 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
           
            if (confirm != DialogResult.Yes) return;
            Pay payForm = new Pay(num);
            DialogResult payResult = payForm.ShowDialog();

            // ④ 결제 완료 확인된 경우에만 충전 신호 전송
            if (payResult != DialogResult.OK) return;


            // 버튼 비활성화 (중복 클릭 방지)
            CreditCard.Enabled = false;
            KaKaoPay.Enabled = false;
            NaverPay.Enabled = false;

            try
            {
                // 서버에 충전 요청
                KioskChargeResult result = await client.ChargeAsync(client.CurrentUserId, _chargeSeconds);

                if (result.Success)
                {
                    MessageBox.Show(
                        "충전이 완료되었습니다!\n" +
                        "충전 시간: " + ChargeTime.Text + "\n" +
                        "남은 시간: " + FormatTimeDisplay(result.NewRemainTime),
                        "충전 완료",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    // ★ TCP 연결 끊기 + 로그인 상태 초기화
                    client.Disconnect();
                    client.LogoutLocal();

                    // ★ 프로그램 재시작 (.NET 6에서는 Environment.ProcessPath 사용)
                    string exePath = Environment.ProcessPath;
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true
                    });
                    Environment.Exit(0);
                    return;
                }
                else
                {
                    MessageBox.Show("충전 실패");
                    //client.Disconnect();
                    //client.LogoutLocal();

                    //// ★ 프로그램 재시작 (.NET 6에서는 Environment.ProcessPath 사용)
                    //string exePath = Environment.ProcessPath;
                    //Process.Start(new ProcessStartInfo
                    //{
                    //    FileName = exePath,
                    //    UseShellExecute = true
                    //});
                    //Environment.Exit(0);
                    //return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("오류: " + ex.Message, "오류");
            }
            finally
            {
                CreditCard.Enabled = true;
                KaKaoPay.Enabled = true;
                NaverPay.Enabled = true;
            }
        }

        // ═══════════════════════════════════════════════
        //  유틸 - bigLabel 텍스트에서 시간 줄만 추출
        //  예: "50,000원\r\n60시간\r\n" → "60시간"
        //      "3,000원\r\n3시간30분\r\n" → "3시간30분"
        // ═══════════════════════════════════════════════
        private string GetTimeLineFromLabel(string labelText)
        {
            if (string.IsNullOrEmpty(labelText)) return ""; //라벨에 글자가 없으면 리턴함

            string[] lines = labelText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);  //텍스트를 줄바꿈기준으로 자름
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if ((trimmed.Contains("시간") || trimmed.Contains("분")) && !trimmed.Contains("원"))
                    return trimmed;
            }
            return "";
        }

        // ═══════════════════════════════════════════════
        //  유틸 - 시간 텍스트를 초(seconds)로 변환
        //  "60시간" → 216000  /  "3시간30분" → 12600
        // ═══════════════════════════════════════════════
        private int ParseTimeToSeconds(string timeText)
        {
            if (string.IsNullOrEmpty(timeText)) return 0;

            int totalSeconds = 0;

            Match hourMatch = Regex.Match(timeText, @"(\d+)\s*시간"); //시간앞의 숫자를 가져옴
            if (hourMatch.Success)
                totalSeconds += int.Parse(hourMatch.Groups[1].Value) * 3600;    //가져온 숫자랑 *3600

            Match minMatch = Regex.Match(timeText, @"(\d+)\s*분");   //분 앞의 숫자를 가져옴
            if (minMatch.Success)
                totalSeconds += int.Parse(minMatch.Groups[1].Value) * 60;   //가져온 숫자 * 60 

            return totalSeconds;
        }

        // ═══════════════════════════════════════════════
        //  유틸 - 초를 "N시간 M분" 형식으로 표시
        // ═══════════════════════════════════════════════
        private string FormatTimeDisplay(int totalSeconds)
        {
            if (totalSeconds <= 0) return "0분";

            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;

            if (hours > 0 && minutes > 0)
                return hours + "시간 " + minutes + "분";
            else if (hours > 0)
                return hours + "시간";
            else
                return minutes + "분";
        }
    }
}