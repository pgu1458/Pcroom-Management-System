using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace test
{
    public partial class CounterAl : UserControl
    {
        private DateTime[] _loginTimes = new DateTime[51];
        private bool[] _seatUsing = new bool[51];
        private string[] _seatUserId = new string[51];
        private int[] _seatRemainSec = new int[51];

        // ── 채팅 UI (textBox2, chat_enter는 Designer에 이미 존재) ──
        private string _selectedChatUserId;
        private int _selectedSeatNum;  // ListView 실시간 갱신용

        // ── 타이머 ──
        private System.Windows.Forms.Timer _seatTimer;

        private string MemberDbPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Member.db");
        private string FoodDbPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Food.db");

        public CounterAl()
        {
            InitializeComponent();
            Load_Food();
            Load_Member();
            SetupOrderGrid();
            InitSeatButtons();
            InitListView();
            InitChatUI();
            LoadSeatStatus();
            UpdateSeatCount();

            TcpServer.Instance.OnSeatLogin += OnSeatLogin;
            TcpServer.Instance.OnSeatLogout += OnSeatLogout;
            TcpServer.Instance.OnOrderReceived += OnOrderReceived;
            TcpServer.Instance.OnMemberRegistered += OnMemberRegistered;
            TcpServer.Instance.OnLog += OnLog;
            TcpServer.Instance.OnChatReceived += OnChatReceived;
            TcpServer.Instance.OnChargeCompleted += OnChargeCompleted;

            // 1초 타이머: 좌석별 이용시간/남은시간 갱신
            _seatTimer = new System.Windows.Forms.Timer();
            _seatTimer.Interval = 1000;
            _seatTimer.Tick += SeatTimer_Tick;
            _seatTimer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!this.Visible)
            {
                TcpServer.Instance.OnSeatLogin -= OnSeatLogin;
                TcpServer.Instance.OnSeatLogout -= OnSeatLogout;
                TcpServer.Instance.OnOrderReceived -= OnOrderReceived;
                TcpServer.Instance.OnMemberRegistered -= OnMemberRegistered;
                TcpServer.Instance.OnLog -= OnLog;
                TcpServer.Instance.OnChatReceived -= OnChatReceived;
                TcpServer.Instance.OnChargeCompleted -= OnChargeCompleted;
                _seatTimer?.Stop();
            }
        }

        // ═══════════════════════════════════════════════
        //  채팅 UI 초기화 (코드로 생성, Designer 수정 없음)
        // ═══════════════════════════════════════════════
        private void InitChatUI()
        {
            // rtChat 위치 조정 (Dock 해제 → 오른쪽 하단에 로그용으로 배치)
            rtChat.Dock = DockStyle.None;
            rtChat.Location = new Point(465, 380);
            rtChat.Size = new Size(850, 125);

            // textBox2는 Designer에 이미 존재 → 입력 가능하게 설정
            textBox2.Multiline = true;

            // chat_enter는 Designer에 이미 존재 → 이벤트만 연결
            chat_enter.Click += ChatEnter_Click;
        }

        // ═══════════════════════════════════════════════
        //  이벤트 핸들러
        // ═══════════════════════════════════════════════
        private void OnSeatLogin(int seatNum, string name, int remainTime, DateTime loginTime)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnSeatLogin(seatNum, name, remainTime, loginTime))); return; }
            _loginTimes[seatNum] = loginTime;
            _seatUsing[seatNum] = true;
            _seatRemainSec[seatNum] = remainTime;
            _seatUserId[seatNum] = GetUserIdBySeat(seatNum);
            SetSeatActive(seatNum, name, remainTime);
            UpdateSeatCount();
        }

        private void OnSeatLogout(int seatNum)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnSeatLogout(seatNum))); return; }
            _loginTimes[seatNum] = default;
            _seatUsing[seatNum] = false;
            _seatRemainSec[seatNum] = 0;
            _seatUserId[seatNum] = null;
            SetSeatInactive(seatNum);
            UpdateSeatCount();
        }

        private void OnOrderReceived(int orderId, string items, int total)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnOrderReceived(orderId, items, total))); return; }
            dataGridView2.Rows.Add(orderId, items, total.ToString("N0") + "원", DateTime.Now.ToString("MM/dd"), DateTime.Now.ToString("HH:mm:ss"));
            Load_Food(); // Food 그리드 새로고침 (판매량/재고량 변동 반영)
        }

        private void OnMemberRegistered(string userId)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnMemberRegistered(userId))); return; }
            Load_Member();
        }

        private void OnLog(string msg)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnLog(msg))); return; }
            if (rtChat != null) { rtChat.AppendText(msg + "\n"); rtChat.ScrollToCaret(); }
        }

        // ★ 채팅 수신 이벤트 (이용자 → 관리자)
        private void OnChatReceived(string userId, string userName, string message)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnChatReceived(userId, userName, message))); return; }
            if (_selectedChatUserId == userId)
            {
                string time = DateTime.Now.ToString("HH:mm");
                textBox2.AppendText("[" + time + "] " + userName + ": " + message + "\r\n");
            }
        }

        // ★ 키오스크 충전 완료 이벤트 (이용 중인 회원의 좌석 remain 라벨 업데이트)
        private void OnChargeCompleted(string userId, int seatNum, int newRemainSec)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => OnChargeCompleted(userId, seatNum, newRemainSec))); return; }

            // 관리자가 메모리에 갖고 있는 좌석별 남은시간 업데이트
            _seatRemainSec[seatNum] = newRemainSec;

            // 좌석표의 remain 라벨 즉시 업데이트
            int h = newRemainSec / 3600;
            int m = (newRemainSec % 3600) / 60;
            int s = newRemainSec % 60;
            SetLabel("remain" + seatNum, h.ToString("D2") + ":" + m.ToString("D2") + ":" + s.ToString("D2"));

            // 현재 선택된 좌석이면 ListView의 남은시간도 갱신
            if (_selectedSeatNum == seatNum)
            {
                foreach (ListViewItem item in listView1.Items)
                {
                    if (item.Text == "남은시간" && item.SubItems.Count > 1)
                    {
                        item.SubItems[1].Text = h.ToString("D2") + ":" + m.ToString("D2");
                        break;
                    }
                }
            }

            // 회원정보 그리드 새로고침 (DB time 값 변동 반영)
            Load_Member();
        }

        // ═══════════════════════════════════════════════
        //  ★ label3 이용석 수 갱신
        // ═══════════════════════════════════════════════
        private void UpdateSeatCount()
        {
            int count = 0;
            for (int i = 1; i <= 50; i++)
                if (_seatUsing[i]) count++;
            label3.Text = count.ToString();
        }

        // ═══════════════════════════════════════════════
        //  ★ 1초 타이머: 좌석별 이용시간/남은시간 실시간 갱신
        // ═══════════════════════════════════════════════
        private void SeatTimer_Tick(object sender, EventArgs e)
        {
            for (int i = 1; i <= 50; i++)
            {
                if (!_seatUsing[i]) continue;

                string usingStr = "-";
                string remainStr = "-";

                // 이용시간 갱신
                if (_loginTimes[i] != default)
                {
                    TimeSpan el = DateTime.Now - _loginTimes[i];
                    usingStr = ((int)el.TotalHours).ToString("D2") + ":" + el.Minutes.ToString("D2") + ":" + el.Seconds.ToString("D2");
                    SetLabel("using" + i, usingStr);
                }

                // 남은시간 감소
                if (_seatRemainSec[i] > 0)
                {
                    _seatRemainSec[i]--;
                    int h = _seatRemainSec[i] / 3600, m = (_seatRemainSec[i] % 3600) / 60, s = _seatRemainSec[i] % 60;
                    remainStr = h.ToString("D2") + ":" + m.ToString("D2") + ":" + s.ToString("D2");
                    SetLabel("remain" + i, remainStr);
                }

                // ★ 현재 선택된 좌석이면 ListView도 실시간 갱신
                if (_selectedSeatNum == i)
                {
                    UpdateListViewTime(usingStr, remainStr);
                }
            }
        }

        // ★ ListView의 남은시간/이용시간 항목 실시간 갱신
        private void UpdateListViewTime(string usingStr, string remainStr)
        {
            foreach (ListViewItem item in listView1.Items)
            {
                if (item.Text == "남은시간" && item.SubItems.Count > 1)
                    item.SubItems[1].Text = remainStr;
                else if (item.Text == "이용시간" && item.SubItems.Count > 1)
                    item.SubItems[1].Text = usingStr;
            }
        }

        private void SetLabel(string name, string text)
        {
            Control[] found = this.Controls.Find(name, true);
            if (found.Length > 0 && found[0] is Label lbl) lbl.Text = text;
        }

        // ═══════════════════════════════════════════════
        //  좌석 관련
        // ═══════════════════════════════════════════════
        private void InitSeatButtons()
        {
            for (int i = 1; i <= 50; i++)
            {
                Control[] found = this.Controls.Find("button" + i, true);
                if (found.Length > 0 && found[0] is Button btn)
                    btn.Click += SeatBtn_Click;
            }
        }

        private void LoadSeatStatus()
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT id, seat_number, name, time FROM Member WHERE seat_number > 0", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        int seatNum = Convert.ToInt32(reader["seat_number"]);
                        string name = reader["name"].ToString();
                        string id = reader["id"].ToString();
                        int remainTime = Convert.ToInt32(reader["time"]);

                        _seatUsing[seatNum] = true;
                        _seatUserId[seatNum] = id;
                        _seatRemainSec[seatNum] = remainTime;
                        _loginTimes[seatNum] = DateTime.Now;

                        SetSeatActive(seatNum, name, remainTime);
                    }
                }
            }
        }

        private void SetSeatActive(int seatNum, string name, int remainSeconds)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetSeatActive(seatNum, name, remainSeconds))); return; }
            Control[] found = this.Controls.Find("button" + seatNum, true);
            if (found.Length > 0 && found[0] is Button btn) { btn.BackColor = Color.Blue; btn.ForeColor = Color.White; }

            // 이름 표시
            SetLabel("name" + seatNum, name);

            // 남은시간 표시
            int h = remainSeconds / 3600, m = (remainSeconds % 3600) / 60, s = remainSeconds % 60;
            SetLabel("remain" + seatNum, h.ToString("D2") + ":" + m.ToString("D2") + ":" + s.ToString("D2"));

            // 이용시간 초기화
            SetLabel("using" + seatNum, "00:00:00");
        }

        private void SetSeatInactive(int seatNum)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetSeatInactive(seatNum))); return; }
            Control[] found = this.Controls.Find("button" + seatNum, true);
            if (found.Length > 0 && found[0] is Button btn) { btn.BackColor = Color.White; btn.ForeColor = Color.Black; }
            SetLabel("name" + seatNum, "-");
            SetLabel("using" + seatNum, "-");
            SetLabel("remain" + seatNum, "-");
        }

        // ★ 좌석 버튼 클릭 → listView에 회원정보 표시
        private void SeatBtn_Click(object sender, EventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null) return;
            string numStr = btn.Name.Replace("button", "");
            int seatNum;
            if (!int.TryParse(numStr, out seatNum)) return;
            ShowSeatInfo(seatNum);
        }

        // ═══════════════════════════════════════════════
        //  ListView (좌석 회원정보 + 클릭→채팅이력)
        // ═══════════════════════════════════════════════
        private void InitListView()
        {
            listView1.View = View.Details;
            listView1.FullRowSelect = true;
            listView1.GridLines = true;
            listView1.Columns.Clear();
            listView1.Columns.Add("항목", 80);
            listView1.Columns.Add("내용", 90);
            listView1.BackColor = Color.White;
            listView1.Font = new Font("맑은 고딕", 9);

            // ★ listView 클릭 → 해당 유저 채팅 이력 표시
            listView1.SelectedIndexChanged += ListView1_SelectedIndexChanged;
        }

        private void ListView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;

            // "아이디" 항목 찾기
            foreach (ListViewItem item in listView1.Items)
            {
                if (item.Text == "아이디" && item.SubItems.Count > 1)
                {
                    string userId = item.SubItems[1].Text;
                    if (userId != "-")
                    {
                        _selectedChatUserId = userId;
                        LoadChatHistory(userId);
                    }
                    break;
                }
            }
        }

        // ★ 채팅 이력 로드 → textBox2에 표시
        private void LoadChatHistory(string userId)
        {
            if (textBox2 == null) return;
            textBox2.Clear();

            List<ChatMessage> history = TcpServer.Instance.GetChatHistory(userId);
            foreach (var msg in history)
            {
                string time = msg.Time.ToString("HH:mm");
                string sender = msg.Sender == "user" ? "이용자" : "관리자";
                textBox2.AppendText("[" + time + "] " + sender + ": " + msg.Message + "\r\n");
            }
        }

        // ★ chat_enter 보내기 버튼 클릭 (textBox2 맨 아래 입력 내용을 전송)
        private void ChatEnter_Click(object sender, EventArgs e)
        {
            if (_selectedChatUserId == null || string.IsNullOrWhiteSpace(_selectedChatUserId))
            {
                MessageBox.Show("좌석을 선택하고 이용자를 선택해주세요.", "알림");
                return;
            }

            // textBox2의 마지막 줄(입력 내용)을 추출
            string allText = textBox2.Text;
            int lastNewline = allText.LastIndexOf('\n');
            string inputMsg = (lastNewline >= 0 ? allText.Substring(lastNewline + 1) : allText).Trim();

            if (inputMsg.Length == 0 || inputMsg.StartsWith("[")) return;

            // 입력 줄 제거 후 포맷된 메시지로 교체
            string prevText = lastNewline >= 0 ? allText.Substring(0, lastNewline + 1) : "";
            string time = DateTime.Now.ToString("HH:mm");
            textBox2.Text = prevText + "[" + time + "] 관리자: " + inputMsg + "\r\n";
            textBox2.SelectionStart = textBox2.Text.Length;

            TcpServer.Instance.SendChatToUser(_selectedChatUserId, inputMsg);
        }

        private void ShowSeatInfo(int seatNum)
        {
            listView1.Items.Clear();
            _selectedChatUserId = null;
            _selectedSeatNum = seatNum;  // ★ 실시간 갱신용

            string usingTime = "-";
            if (_seatUsing[seatNum] && _loginTimes[seatNum] != default)
            {
                TimeSpan elapsed = DateTime.Now - _loginTimes[seatNum];
                usingTime = elapsed.Hours.ToString("D2") + ":" + elapsed.Minutes.ToString("D2");
            }

            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT name, id, phone, time FROM Member WHERE seat_number=@seat", conn))
                {
                    cmd.Parameters.AddWithValue("@seat", seatNum);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string userId = reader["id"].ToString();
                            _selectedChatUserId = userId;  // 채팅 대상 자동 설정

                            AddListViewRow("좌석번호", seatNum + "번", Color.FromArgb(230, 240, 255));
                            AddListViewRow("이름", reader["name"].ToString(), Color.White);
                            AddListViewRow("아이디", userId, Color.FromArgb(230, 240, 255));
                            AddListViewRow("전화번호", reader["phone"].ToString(), Color.White);
                            AddListViewRow("남은시간", GetTimeString(Convert.ToInt32(reader["time"])), Color.FromArgb(230, 240, 255));
                            AddListViewRow("이용시간", usingTime, Color.White);

                            // ★ 해당 유저 채팅 이력 로드
                            LoadChatHistory(userId);
                        }
                        else
                        {
                            AddListViewRow("좌석번호", seatNum + "번 (비어있음)", Color.FromArgb(245, 245, 245));
                            AddListViewRow("이름", "-", Color.White);
                            AddListViewRow("아이디", "-", Color.FromArgb(245, 245, 245));
                            AddListViewRow("전화번호", "-", Color.White);
                            AddListViewRow("남은시간", "-", Color.FromArgb(245, 245, 245));
                            AddListViewRow("이용시간", "-", Color.White);

                            if (textBox2 != null) textBox2.Clear();
                        }
                    }
                }
            }
        }

        private void AddListViewRow(string label, string value, Color backColor)
        {
            ListViewItem item = new ListViewItem(label);
            item.SubItems.Add(value);
            item.BackColor = backColor;
            switch (label)
            {
                case "남은시간": item.ForeColor = Color.DarkBlue; item.Font = new Font("맑은 고딕", 9, FontStyle.Bold); break;
                case "이용시간": item.ForeColor = Color.DarkGreen; item.Font = new Font("맑은 고딕", 9, FontStyle.Bold); break;
                case "좌석번호": item.ForeColor = Color.DarkRed; item.Font = new Font("맑은 고딕", 9, FontStyle.Bold); break;
            }
            listView1.Items.Add(item);
        }

        // ── DB 헬퍼 ──
        private string GetUserIdBySeat(int seatNum)
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT id FROM Member WHERE seat_number=@s", conn))
                {
                    cmd.Parameters.AddWithValue("@s", seatNum);
                    object r = cmd.ExecuteScalar();
                    return r != null ? r.ToString() : null;
                }
            }
        }

        // ═══════════════════════════════════════════════
        //  탭2 - 상품주문 (Food.db + 주문목록)
        // ═══════════════════════════════════════════════
        private void Load_Food()
        {
            string cs = "Data Source=" + FoodDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT * FROM Food;", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    DataTable dt = new DataTable();
                    dt.Load(reader);
                    dataGridView1.DataSource = dt;
                    dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                    dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                }
            }
        }

        private void SetupOrderGrid()
        {
            dataGridView2.Columns.Clear();
            dataGridView2.Columns.Add("OrderId", "주문번호");
            dataGridView2.Columns.Add("Items", "주문내역");
            dataGridView2.Columns.Add("Total", "총액");
            dataGridView2.Columns.Add("Date", "날짜");
            dataGridView2.Columns.Add("Time", "시간");
            dataGridView2.AllowUserToAddRows = false;
            dataGridView2.ReadOnly = true;
            dataGridView2.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView2.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }

        // ═══════════════════════════════════════════════
        //  탭3 - 회원정보 (Member.db)
        // ═══════════════════════════════════════════════
        private void Load_Member()
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                using (var cmd = new SQLiteCommand("SELECT * FROM Member;", conn))
                using (var reader = cmd.ExecuteReader())
                {
                    DataTable dt = new DataTable();
                    dt.Load(reader);
                    dataGridView3.DataSource = dt;
                    dataGridView3.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                    dataGridView3.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                }
            }
        }

        // ═══════════════════════════════════════════════
        //  유틸
        // ═══════════════════════════════════════════════
        private string GetTimeString(int seconds)
        {
            int h = seconds / 3600, m = (seconds % 3600) / 60;
            return h.ToString("D2") + ":" + m.ToString("D2");
        }

        // ── btnFix: 회원정보 수정 저장 ──
        private void btnFix_Click_1(object sender, EventArgs e)
        {
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                foreach (DataGridViewRow row in dataGridView3.Rows)
                {
                    if (row.IsNewRow || row.Cells["number"].Value == null) continue;
                    try
                    {
                        int number = Convert.ToInt32(row.Cells["number"].Value);
                        string name = row.Cells["name"].Value?.ToString() ?? "";
                        string id = row.Cells["id"].Value?.ToString() ?? "";
                        string password = row.Cells["password"].Value?.ToString() ?? "";
                        int time = Convert.ToInt32(row.Cells["time"].Value);
                        string birth = row.Cells["birth"].Value?.ToString() ?? "";
                        string phone = row.Cells["phone"].Value?.ToString() ?? "";
                        int seatNumber = Convert.ToInt32(row.Cells["seat_number"].Value);
                        string role = row.Cells["role"].Value?.ToString() ?? "손님";

                        using (var cmd = new SQLiteCommand(@"UPDATE Member SET
                            name=@name, id=@id, password=@pw, time=@time,
                            birth=@birth, phone=@phone, seat_number=@seat, role=@role
                            WHERE number=@number;", conn))
                        {
                            cmd.Parameters.AddWithValue("@number", number);
                            cmd.Parameters.AddWithValue("@name", name);
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.Parameters.AddWithValue("@pw", password);
                            cmd.Parameters.AddWithValue("@time", time);
                            cmd.Parameters.AddWithValue("@birth", birth);
                            cmd.Parameters.AddWithValue("@phone", phone);
                            cmd.Parameters.AddWithValue("@seat", seatNumber);
                            cmd.Parameters.AddWithValue("@role", role);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch { continue; }
                }
            }
            MessageBox.Show("저장 완료!");
            Load_Member();
        }

        // ── btnDelete: 선택 행 삭제 ──
        private void btnDelete_Click_1(object sender, EventArgs e)
        {
            if (dataGridView3.SelectedRows.Count == 0)
            { MessageBox.Show("삭제할 항목을 선택해주세요."); return; }
            if (MessageBox.Show("삭제하시겠습니까?", "확인", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                foreach (DataGridViewRow row in dataGridView3.SelectedRows)
                {
                    if (row.Cells["number"].Value == null) continue;
                    int number = Convert.ToInt32(row.Cells["number"].Value);
                    using (var cmd = new SQLiteCommand("DELETE FROM Member WHERE number=@number;", conn))
                    {
                        cmd.Parameters.AddWithValue("@number", number);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            MessageBox.Show("삭제 완료!");
            Load_Member();
        }

        // ── button51: 전화번호 뒷자리 검색 ──
        private void button51_Click_1(object sender, EventArgs e)
        {
            string search = textBox1.Text.Trim();
            string cs = "Data Source=" + MemberDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                string query = search.Length == 0
                    ? "SELECT * FROM Member;"
                    : "SELECT * FROM Member WHERE phone LIKE @search;";
                using (var cmd = new SQLiteCommand(query, conn))
                {
                    if (search.Length > 0) cmd.Parameters.AddWithValue("@search", "%-" + search);
                    using (var reader = cmd.ExecuteReader())
                    {
                        DataTable dt = new DataTable();
                        dt.Load(reader);
                        dataGridView3.DataSource = dt;
                    }
                }
            }
        }

        // ── btn2: 주문 목록 선택 행 삭제 ──
        private void btn2_Click_1(object sender, EventArgs e)
        {
            if (dataGridView2.SelectedRows.Count == 0) return;
            foreach (DataGridViewRow row in dataGridView2.SelectedRows)
                if (!row.IsNewRow) dataGridView2.Rows.Remove(row);
        }

        // ── btn1: Food.db 수정 저장 ──
        private void btn1_Click_1(object sender, EventArgs e)
        {
            string cs = "Data Source=" + FoodDbPath + ";Version=3;";
            using (var conn = new SQLiteConnection(cs))
            {
                conn.Open();
                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    if (row.IsNewRow || row.Cells["id"].Value == null) continue;
                    try
                    {
                        int id = Convert.ToInt32(row.Cells["id"].Value);
                        string pName = row.Cells["product_name"].Value?.ToString() ?? "";
                        int price = Convert.ToInt32(row.Cells["price"].Value);
                        int arrival = Convert.ToInt32(row.Cells["arrival"].Value);
                        int inventory = Convert.ToInt32(row.Cells["inventory"].Value);
                        int sale = Convert.ToInt32(row.Cells["sale"].Value);
                        string date = row.Cells["date"].Value?.ToString() ?? "";
                        int hour = Convert.ToInt32(row.Cells["hour"].Value);

                        using (var cmd = new SQLiteCommand(@"UPDATE Food SET
                            product_name=@pn, price=@pr, arrival=@ar,
                            inventory=@inv, sale=@sa, date=@dt, hour=@hr
                            WHERE id=@id;", conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.Parameters.AddWithValue("@pn", pName);
                            cmd.Parameters.AddWithValue("@pr", price);
                            cmd.Parameters.AddWithValue("@ar", arrival);
                            cmd.Parameters.AddWithValue("@inv", inventory);
                            cmd.Parameters.AddWithValue("@sa", sale);
                            cmd.Parameters.AddWithValue("@dt", date);
                            cmd.Parameters.AddWithValue("@hr", hour);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch { continue; }
                }
            }
            MessageBox.Show("저장 완료!");
            Load_Food();
        }

        private void btn_logOut_Click(object sender, EventArgs e)
        {
            this.Controls.Clear();
            CounterLogin counter = new CounterLogin();
            counter.Dock = DockStyle.Fill;
            this.Controls.Add(counter);
        }
    }
}