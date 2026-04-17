using Kiosk_Sum;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography.Xml;
using System.Windows.Forms;

namespace Kiosk_Sum
{
    public partial class KioskSeat : UserControl
    {
        public static int cnt = 0;
        public static int result = 0;
        // ── 좌석 상태 저장 ──
        private bool[] _seatActive = new bool[51];      // index 1~50
        private int[] _seatRemainSec = new int[51];      // 좌석별 남은시간(초)

        // ── 타이머 ──
        private System.Windows.Forms.Timer _countdownTimer;  // 1초 카운트다운
        private System.Windows.Forms.Timer _reconnectTimer;  // 5초 재연결 체크

        // ── 좌석번호 라벨 캐시 (불규칙 네이밍이므로 초기화 시 매핑) ──
        private Dictionary<int, Label> _seatNumberLabels = new Dictionary<int, Label>();

        public KioskSeat()
        {
            InitializeComponent();
            this.Load += (s, e) =>
            {
                panel51.Dock = DockStyle.None;
                panel51.AutoSize = false;
                panel51.Size = new Size(2500, 1115);

                panel52.Size = new Size(2500, 1080);
            };

            // 좌석번호 라벨 매핑
            CacheSeatNumberLabels();

            // 모든 좌석을 초기 비활성화 상태로 설정
            for (int i = 1; i <= 50; i++)
            {
                _seatActive[i] = false;
                _seatRemainSec[i] = 0;
                SetSeatInactive(i);
            }
            cnt = 0;
            // 서버 이벤트 구독: SEATS 데이터 수신 시 UI 갱신
            KioskTcpClient.Instance.OnSeatsUpdated += OnSeatsUpdated;

            // 서버 연결 및 초기 좌석 데이터 수신
            _ = ConnectAndLoadSeatsAsync();

            // ── 1초 카운트다운 타이머 (활성 좌석의 남은시간 감소 + 표시) ──
            _countdownTimer = new System.Windows.Forms.Timer();
            _countdownTimer.Interval = 1000;
            _countdownTimer.Tick += CountdownTimer_Tick;
            _countdownTimer.Start();

            // ── 5초 재연결 타이머 ──
            _reconnectTimer = new System.Windows.Forms.Timer();
            _reconnectTimer.Interval = 5000;
            _reconnectTimer.Tick += ReconnectTimer_Tick;
            _reconnectTimer.Start();

            // ── 컨트롤 파괴 시 정리 ──
            this.HandleDestroyed += KioskSeat_HandleDestroyed;
        }

        // ═══════════════════════════════════════════════
        //  좌석번호 라벨 캐싱
        //  Designer에서 좌석번호 라벨 이름이 불규칙(label2, label6, label12...)
        //  → 각 panel{i} 내부에서 Text가 좌석번호인 Label을 찾아 딕셔너리에 저장
        // ═══════════════════════════════════════════════
        private void CacheSeatNumberLabels()
        {
            for (int i = 1; i <= 50; i++)
            {
                Control[] panelFound = this.Controls.Find("panel" + i, true);
                if (panelFound.Length == 0) continue;

                string seatText = i.ToString();
                foreach (Control c in panelFound[0].Controls)
                {
                    if (c is Label lbl && lbl.Text.Trim() == seatText)
                    {
                        if (lbl.Name != "name" + i &&
                            lbl.Name != "remain" + i &&
                            lbl.Name != "using" + i &&
                            lbl.Name != "remainlabel" + i &&
                            lbl.Name != "usinglabel" + i)
                        {
                            _seatNumberLabels[i] = lbl;
                            break;
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════
        //  서버 연결 → 초기 좌석 데이터 수신
        // ═══════════════════════════════════════════════
        private async System.Threading.Tasks.Task ConnectAndLoadSeatsAsync()
        {
            await KioskTcpClient.Instance.ConnectAsync();
            // 연결 성공 시 서버가 자동으로 SEATS 데이터를 보내줌
            // → OnSeatsUpdated 이벤트로 수신 처리됨
        }

        // ═══════════════════════════════════════════════
        //  SEATS 데이터 수신 이벤트 핸들러
        //  관리자PC에서 사용자 PC 온/오프 이벤트 발생 시
        //  서버가 SEATS 데이터를 다시 보내줌 → 여기서 처리
        // ═══════════════════════════════════════════════
        private void OnSeatsUpdated(Dictionary<int, SeatInfo> seats)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnSeatsUpdated(seats)));
                return;
            }
            cnt = 0;
            foreach (var kv in seats)
            {
                int seatNum = kv.Key;
                SeatInfo info = kv.Value;

                if (seatNum < 1 || seatNum > 50) continue;

                _seatActive[seatNum] = info.Active;
                _seatRemainSec[seatNum] = info.RemainSeconds;

                if (info.Active)
                {

                    SetSeatActive(seatNum);

                    cnt++;
                }
                else
                {
                    SetSeatInactive(seatNum);
                }

                //cnt = cnt - result;
            }
        }

        // ═══════════════════════════════════════════════
        //  1초 카운트다운 타이머
        //  활성 좌석의 남은시간을 1초씩 감소시키고 UI에 표시
        // ═══════════════════════════════════════════════
        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            for (int i = 1; i <= 50; i++)
            {
                if (!_seatActive[i]) continue;

                // 남은시간 감소
                if (_seatRemainSec[i] > 0)
                    _seatRemainSec[i]--;

                // remain 라벨에 남은시간 표시
                SetLabelText("remain" + i, FormatTime(_seatRemainSec[i]));
            }
        }

        // ═══════════════════════════════════════════════
        //  좌석 활성화 표시
        //  - 버튼 배경: 파란색
        //  - 모든 라벨: 흰색 글씨 + 파란색 배경
        //  - name 라벨: "사용중"
        //  - remain 라벨: 남은시간 표시
        // ═══════════════════════════════════════════════
        private void SetSeatActive(int seatNum)
        {
            // 버튼 배경색 변경
            Control[] btnFound = this.Controls.Find("button" + seatNum, true);
            if (btnFound.Length > 0 && btnFound[0] is Button btn)
            {
                btn.BackColor = Color.Blue;
                btn.ForeColor = Color.White;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;
                //cnt++;
                label1.Text = cnt.ToString() + "/50석";
            }

            // 패널 내 모든 라벨: 흰색 글씨 + 파란색 배경
            Control[] panelFound = this.Controls.Find("panel" + seatNum, true);
            if (panelFound.Length > 0)
            {
                foreach (Control c in panelFound[0].Controls)
                {
                    if (c is Label lbl)
                    {
                        lbl.ForeColor = Color.White;
                        lbl.BackColor = Color.Blue;
                    }
                }
            }

            // 라벨 텍스트 설정
            SetLabelText("name" + seatNum, "사용중");
            SetLabelText("remainlabel" + seatNum, "남은시간 :");
            SetLabelText("remain" + seatNum, FormatTime(_seatRemainSec[seatNum]));

            // 이용시간 라벨은 키오스크에서는 비움 (관리자만 표시)
            SetLabelText("usinglabel" + seatNum, "");
            SetLabelText("using" + seatNum, "");
        }

        // ═══════════════════════════════════════════════
        //  좌석 비활성화 표시
        //  - 버튼 배경: 검은색
        //  - 좌석번호만 흰색으로 표시
        //  - 나머지 라벨 텍스트 지우기
        // ═══════════════════════════════════════════════
        private void SetSeatInactive(int seatNum)
        {
            // 버튼 배경색 변경
            Control[] btnFound = this.Controls.Find("button" + seatNum, true);
            if (btnFound.Length > 0 && btnFound[0] is Button btn)
            {
                btn.BackColor = Color.Black;
                btn.ForeColor = Color.White;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;

                label1.Text = cnt.ToString() + "/50석";
            }

            // 패널 내 모든 라벨: 숨김 처리 (배경 검정 + 글씨 검정)
            Control[] panelFound = this.Controls.Find("panel" + seatNum, true);
            if (panelFound.Length > 0)
            {
                foreach (Control c in panelFound[0].Controls)
                {
                    if (c is Label lbl)
                    {
                        lbl.ForeColor = Color.Black;
                        lbl.BackColor = Color.Black;
                    }
                }
            }

            // 좌석번호 라벨만 흰색으로 다시 표시
            if (_seatNumberLabels.ContainsKey(seatNum))
            {
                _seatNumberLabels[seatNum].ForeColor = Color.White;
                _seatNumberLabels[seatNum].BackColor = Color.Black;
            }

            // 나머지 라벨 텍스트 초기화
            SetLabelText("name" + seatNum, "");
            SetLabelText("remain" + seatNum, "");
            SetLabelText("using" + seatNum, "");
            SetLabelText("remainlabel" + seatNum, "");
            SetLabelText("usinglabel" + seatNum, "");
        }

        // ═══════════════════════════════════════════════
        //  재연결 타이머 (5초)
        // ═══════════════════════════════════════════════
        private async void ReconnectTimer_Tick(object sender, EventArgs e)
        {
            if (!KioskTcpClient.Instance.IsConnected)
            {
                await KioskTcpClient.Instance.ConnectAsync();
            }
        }

        // ═══════════════════════════════════════════════
        //  유틸
        // ═══════════════════════════════════════════════
        private void SetLabelText(string name, string text)
        {
            Control[] found = this.Controls.Find(name, true);
            if (found.Length > 0 && found[0] is Label lbl)
            {
                lbl.Text = text;
            }
        }

        /// <summary>초를 HH:MM:SS 형식으로 변환</summary>
        private string FormatTime(int totalSeconds)
        {
            if (totalSeconds <= 0) return "00:00:00";
            int h = totalSeconds / 3600;
            int m = (totalSeconds % 3600) / 60;
            int s = totalSeconds % 60;
            return h.ToString("D2") + ":" + m.ToString("D2") + ":" + s.ToString("D2");
        }

        // ═══════════════════════════════════════════════
        //  정리
        // ═══════════════════════════════════════════════
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!this.Visible)
            {
                KioskTcpClient.Instance.OnSeatsUpdated -= OnSeatsUpdated;
                _countdownTimer?.Stop();
                _reconnectTimer?.Stop();
            }
        }

        private void KioskSeat_HandleDestroyed(object sender, EventArgs e)
        {
            KioskTcpClient.Instance.OnSeatsUpdated -= OnSeatsUpdated;
            _countdownTimer?.Stop();
            _countdownTimer?.Dispose();
            _reconnectTimer?.Stop();
            _reconnectTimer?.Dispose();
        }

        private void button51_Click(object sender, EventArgs e)
        {
            var client = KioskTcpClient.Instance;
            client.Disconnect();
            client.LogoutLocal();
            string exePath = Environment.ProcessPath;
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true
            });
            Environment.Exit(0);
            return;
        }

        private void panel52_Paint(object sender, PaintEventArgs e)
        {

        }

        // ※ Dispose는 KioskSeat_Designer.cs에 이미 존재하므로 여기서 정의하지 않음
    }
}