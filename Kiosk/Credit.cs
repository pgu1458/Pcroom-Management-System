using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Kiosk_Sum
{
    public partial class Credit : UserControl
    {
        public event Action OnConfirmed;  // 결제 완료
        public event Action OnCancelled;  // 취소 or 타임아웃

        int count;

        public Credit()
        {
            InitializeComponent();
            btnComplete.Click += (s, e) => ConfirmPayment();
        }

        private void Credit_Load(object sender, EventArgs e)
        {
            timer1.Interval = 1000;
            timer2.Interval = 100;
            timer1.Start();
            timer2.Start();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            count++;
            
            if (count >= 30)
            {
                timer1.Stop();
                OnCancelled?.Invoke();   // 30초 타임아웃 → 취소
            }
        }

        // Designer에서 "결제 완료" 버튼 클릭 시 연결
        // btnComplete.Click += (s, e) => ConfirmPayment();
        public void ConfirmPayment()
        {
            timer1.Stop();
            OnConfirmed?.Invoke();       // 결제 완료 → 충전 진행
        }

        private void timer2_Tick(object sender, EventArgs e)
        {
            bigLabel1.Text = "00:" + (30 - count).ToString("D2");
            if(count>15&&count<25)
            {
                bigLabel1.ForeColor = Color.Yellow;
            }
            else if(count>25)
            {
                bigLabel1.ForeColor = Color.Red;
            }
        }
    }
}
