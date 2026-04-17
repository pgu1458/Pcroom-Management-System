using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace test
{
    public partial class Admin : UserControl
    {
        public Admin()
        {
            InitializeComponent();
        }

        private void Admin_Load(object sender, EventArgs e)
        {
            InitComboBox();     // 콤보박스 초기화
            Load_Food();        // Food.db 그리드뷰 로드
        }
        private void InitComboBox()
        {
            // 월 콤보박스 1~12월
            comboBox1.Items.Clear();
            for (int i = 1; i <= 12; i++)
                comboBox1.Items.Add(i.ToString("D2"));
            comboBox1.SelectedIndex = 0;
        }

        // 월 선택 시 일 콤보박스 업데이트


        // ───────────────────────────────────────
        // 보기 버튼 클릭 - 차트 그리기
        // ───────────────────────────────────────


        // ───────────────────────────────────────
        // 차트 그리기
        // ───────────────────────────────────────
        private void Load_Chart(string month, string date)
        {
            // ★ sales.db 사용
            string salesDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sales.db");
            string connString = "Data Source=" + salesDbPath + ";Version=3;";

            // 차트 초기화
            chart1.Series.Clear();
            chart1.ChartAreas.Clear();
            chart1.Titles.Clear();

            chart1.ChartAreas.Add(new ChartArea("Main"));

            var series = new Series("매출");
            series.ChartType = SeriesChartType.Column;
            series.Color = Color.SteelBlue;

            long totalSales = 0;

            using (SQLiteConnection conn = new SQLiteConnection(connString))
            {
                conn.Open();

                string query;

                if (date == "-전체-")
                {
                    // 월 전체 - 일별 매출 합계
                    query = @"
                        SELECT date, SUM(price * sale) AS total_sales
                        FROM sales_data
                        WHERE date LIKE @month || '-%'
                        GROUP BY date
                        ORDER BY date;";
                }
                else
                {
                    // 특정 일 - 시간대별 매출 합계
                    query = @"
                        SELECT hour, SUM(price * sale) AS total_sales
                        FROM sales_data
                        WHERE date = @month || '-' || @date
                        GROUP BY hour
                        ORDER BY hour;";
                }

                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@month", month);
                    cmd.Parameters.AddWithValue("@date", date);

                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string xLabel = date == "-전체-"
                                ? reader["date"].ToString()
                                : reader["hour"].ToString() + "시";

                            long sales = Convert.ToInt64(reader["total_sales"]);
                            totalSales += sales;

                            series.Points.AddXY(xLabel, sales);
                        }
                    }
                }
            }

            chart1.Series.Add(series);

            // 차트 제목
            string title = date == "-전체-"
                ? month + "월 전체 매출"
                : month + "월 " + date + "일 매출";
            chart1.Titles.Add(title);

            chart1.ChartAreas["Main"].AxisX.Title = date == "-전체-" ? "날짜" : "시간";
            chart1.ChartAreas["Main"].AxisY.Title = "매출(원)";
            chart1.ChartAreas["Main"].AxisY.LabelStyle.Format = "#,0";
            chart1.ChartAreas["Main"].AxisX.LabelStyle.Angle = -45;

            // 총 매출 textBox1에 표시
            textBox1.Text = string.Format("{0:N0}", totalSales) + " 원";
        }

        // ───────────────────────────────────────
        // Food.db 그리드뷰 로드
        // ───────────────────────────────────────
        private void Load_Food()
        {
            string foodDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Food.db");
            string connString = "Data Source=" + foodDbPath + ";Version=3;";

            using (SQLiteConnection conn = new SQLiteConnection(connString))
            {
                conn.Open();
                string query = "SELECT * FROM Food;";

                using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                {
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        DataTable dt = new DataTable();
                        dt.Load(reader);
                        dataGridView1.DataSource = dt;
                        dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                        dataGridView1.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                    }
                }
            }
        }

        // ───────────────────────────────────────
        // 저장 버튼 클릭 - 그리드뷰 내용을 DB에 저장
        // ───────────────────────────────────────


        private void btnCheck_Click(object sender, EventArgs e)
        {
            if (comboBox1.SelectedItem == null || comboBox2.SelectedItem == null)
            {
                MessageBox.Show("월과 일을 선택해주세요.");
                return;
            }

            string month = comboBox1.SelectedItem.ToString();
            string date = comboBox2.SelectedItem.ToString();

            Load_Chart(month, date);
        }

        private void btnSave_Click_1(object sender, EventArgs e)
        {
            string foodDbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Food.db");
            string connString = "Data Source=" + foodDbPath + ";Version=3;";

            using (SQLiteConnection conn = new SQLiteConnection(connString))
            {
                conn.Open();

                foreach (DataGridViewRow row in dataGridView1.Rows)
                {
                    if (row.IsNewRow) continue;
                    if (row.Cells["id"].Value == null) continue;

                    try
                    {
                        int id = Convert.ToInt32(row.Cells["id"].Value);
                        string productName = row.Cells["product_name"].Value?.ToString() ?? "";
                        int price = Convert.ToInt32(row.Cells["price"].Value);
                        int arrival = Convert.ToInt32(row.Cells["arrival"].Value);
                        int inventory = Convert.ToInt32(row.Cells["inventory"].Value);
                        int sale = Convert.ToInt32(row.Cells["sale"].Value);
                        string date = row.Cells["date"].Value?.ToString() ?? "";
                        int hour = Convert.ToInt32(row.Cells["hour"].Value);

                        string query = @"
                            UPDATE Food SET
                                product_name = @product_name,
                                price        = @price,
                                arrival      = @arrival,
                                inventory    = @inventory,
                                sale         = @sale,
                                date         = @date,
                                hour         = @hour
                            WHERE id = @id;";

                        using (SQLiteCommand cmd = new SQLiteCommand(query, conn))
                        {
                            cmd.Parameters.AddWithValue("@id", id);
                            cmd.Parameters.AddWithValue("@product_name", productName);
                            cmd.Parameters.AddWithValue("@price", price);
                            cmd.Parameters.AddWithValue("@arrival", arrival);
                            cmd.Parameters.AddWithValue("@inventory", inventory);
                            cmd.Parameters.AddWithValue("@sale", sale);
                            cmd.Parameters.AddWithValue("@date", date);
                            cmd.Parameters.AddWithValue("@hour", hour);
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            MessageBox.Show("저장 완료!");
            Load_Food(); // 그리드뷰 새로고침
        }

        private void comboBox1_SelectedIndexChanged_1(object sender, EventArgs e)
        {
            if (comboBox1.SelectedItem == null) return;

            int month = int.Parse(comboBox1.SelectedItem.ToString());
            int daysInMonth = DateTime.DaysInMonth(2026, month);

            comboBox2.Items.Clear();
            comboBox2.Items.Add("-전체-");
            for (int day = 1; day <= daysInMonth; day++)
                comboBox2.Items.Add(day.ToString("D2"));

            comboBox2.SelectedIndex = 0;
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